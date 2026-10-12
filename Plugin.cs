using AutoFete.UI;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AutoFete;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly IFateTable fates;
    private readonly Navmesh navmesh;
    private readonly GameActions actions;
    private readonly FeteActionAutomation automation;
    private readonly HandinDialogs handinDialogs;
    private readonly CaptureRecorder recorder;
    private readonly FeteController controller;
    private readonly Configuration config;
    private readonly WindowSystem windows = new("AutoFete");
    private readonly RunnerWindow window;
    private readonly SettingsWindow settingsWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        ICommandManager commands,
        IChatGui chat,
        IPluginLog log,
        IClientState clientState,
        IObjectTable objects,
        IFateTable fates,
        ITargetManager targets,
        ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.commands = commands;
        this.chat = chat;
        this.log = log;
        this.clientState = clientState;
        this.fates = fates;
        StatusText.SetLanguage(clientState.ClientLanguage);
        var configPath = pluginInterface.ConfigFile.FullName;
        var loadedConfig = false;
        try
        {
            config = Configuration.Load(configPath);
            loadedConfig = true;
        }
        catch (Exception e)
        {
            config = new Configuration();
            log.Error(e, "Could not read AutoFete settings");
        }
        if (loadedConfig)
            SaveConfig();
        navmesh = new Navmesh(pluginInterface);
        actions = new GameActions(targets, objects, condition);
        automation = new FeteActionAutomation(actions, navmesh, objects);
        handinDialogs = new HandinDialogs(pluginInterface);
        controller = new FeteController(clientState, objects, fates, navmesh, actions,
            automation, handinDialogs, config);
        controller.StageCompleted += OnStageCompleted;
        controller.AutomationFailed += OnAutomationFailed;
        recorder = new CaptureRecorder(clientState, objects, fates, targets, controller, actions);
        window = new RunnerWindow(controller, recorder, config, ActiveStageLabel,
            OpenSettingsWindow, ToggleRecording,
            () => OnCommand("/autofete", controller.IsArmed ? "stop" : "start"));
        settingsWindow = new SettingsWindow(config, recorder, SaveConfig, StopRecording);
        windows.AddWindow(window);
        windows.AddWindow(settingsWindow);
        commands.AddHandler("/autofete", new CommandInfo(OnCommand)
        {
            HelpMessage = "/autofete start|stop|debug",
        });
        framework.Update += OnUpdate;
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenWindow;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsWindow;
        commands.RemoveHandler("/autofete");
        controller.StageCompleted -= OnStageCompleted;
        controller.AutomationFailed -= OnAutomationFailed;
        controller.Stop(StatusText.Get("Stop_PluginUnloaded"));
        handinDialogs.Dispose();
        if (recorder.IsRecording)
            StopRecording();
        windows.RemoveAllWindows();
    }

    private void OpenWindow() => window.IsOpen = true;
    private void OpenSettingsWindow() => settingsWindow.IsOpen = true;

    private void SaveConfig()
    {
        try { config.Save(pluginInterface.ConfigFile.FullName); }
        catch (Exception e)
        {
            log.Error(e, "Could not save AutoFete settings");
            chat.PrintError("ERROR: Could not save settings; see /xllog", "AutoFete");
        }
    }

    private string ActiveStageLabel()
    {
        var known = FeteCatalog.WithDefinitions(fates)
            .Where(x => x.Stage != null)
            .ToArray();
        var running = known.FirstOrDefault(x => x.Fate.State == FateState.Running);
        if (running.Fate != null)
            return StatusText.Format(
                controller.IsAutomatingStage && controller.CurrentStage == running.Stage!.Value.Stage
                    ? "Ui_StageAutomating"
                    : "Ui_StageRunning",
                running.Stage!.Value.Name);

        var ending = known.FirstOrDefault(x =>
            x.Fate.State == FateState.Ending && x.Stage!.Value.Stage == controller.CurrentStage);
        if (ending.Fate != null)
            return StatusText.Format("Ui_StageEnding", ending.Stage!.Value.Name);

        var preparing = known.FirstOrDefault(x => x.Fate.State == FateState.Preparing);
        return preparing.Fate == null
            ? StatusText.Get("Ui_NoStage")
            : StatusText.Format("Ui_StagePreparing", preparing.Stage!.Value.Name);
    }

    private void OnUpdate(IFramework _)
    {
        try
        {
            if (recorder.IsRecording && clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
                StopRecording();
            controller.Tick();
            recorder.Tick();
            handinDialogs.Tick(controller.WantsHandinDialogs);
        }
        catch (Exception e)
        {
            controller.StopWithError(StatusText.Get("Stop_UnexpectedError"));
            handinDialogs.Disable();
            log.Error(e, "AutoFete update failed");
        }
    }

    private void OnStageCompleted(string message) => chat.Print(message, "AutoFete");
    private void OnAutomationFailed(string message) => chat.PrintError(message, "AutoFete");

    private void OnCommand(string _, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "start":
                if (!controller.Start(config.Mode, join: true))
                    chat.PrintError(controller.Status, "AutoFete");
                else
                    chat.Print(StatusText.Get("Result_AutomationStarted"), "AutoFete");
                break;
            case "stop":
                controller.Stop();
                chat.Print(StatusText.Get("Stop_Default"), "AutoFete");
                break;
            case "debug":
                if (!config.DebugEnabled)
                {
                    config.DebugEnabled = true;
                    SaveConfig();
                }
                OpenWindow();
                break;
            default:
                OpenWindow();
                break;
        }
    }

    private void ToggleRecording()
    {
        if (recorder.IsRecording)
        {
            StopRecording();
            return;
        }

        if (!clientState.IsLoggedIn || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
        {
            chat.PrintError(StatusText.Get("Error_EnterFirmament"), "AutoFete");
            return;
        }

        recorder.Start();
        chat.Print("Capture started", "AutoFete");
    }

    private void StopRecording()
    {
        try
        {
            var path = recorder.Stop(pluginInterface.GetPluginConfigDirectory());
            chat.Print($"Capture saved: {path}", "AutoFete");
        }
        catch (Exception e)
        {
            log.Error(e, "Could not save Fête capture");
            chat.PrintError("ERROR: Could not save capture; see /xllog", "AutoFete");
        }
    }
}
