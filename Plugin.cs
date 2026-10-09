using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using System.Numerics;

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
        controller = new FeteController(clientState, objects, fates, navmesh, actions, automation);
        recorder = new CaptureRecorder(clientState, objects, fates, targets, controller, actions);
        window = new RunnerWindow(this);
        settingsWindow = new SettingsWindow(this);
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
        controller.Stop("Plugin unloaded.");
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
            chat.PrintError("Could not save settings. See /xllog.", "AutoFete");
        }
    }

    private string ActiveStageLabel()
    {
        var running = fates.FirstOrDefault(f =>
            f.State == FateState.Running && FeteCatalog.Find(f.FateId) != null);
        if (running != null)
            return FeteCatalog.Find(running.FateId)!.Value.Name;

        var ending = fates.FirstOrDefault(f =>
            f.State == FateState.Ending && FeteCatalog.Find(f.FateId)?.Stage == controller.CurrentStage);
        if (ending != null)
            return $"{FeteCatalog.Find(ending.FateId)!.Value.Name} (ending)";

        var preparing = fates.FirstOrDefault(f =>
            f.State == FateState.Preparing && FeteCatalog.Find(f.FateId) != null);
        return preparing == null
            ? "None"
            : $"{FeteCatalog.Find(preparing.FateId)!.Value.Name} (preparing)";
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
            controller.Stop("Stopped after an unexpected error. See /xllog.");
            handinDialogs.Disable();
            log.Error(e, "AutoFete update failed");
        }
    }

    private void OnCommand(string _, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "start":
                if (!controller.Start(config.Mode, join: true))
                    chat.PrintError(controller.Status, "AutoFete");
                else
                    chat.Print("Fête automation started.", "AutoFete");
                break;
            case "stop":
                controller.Stop();
                chat.Print("Stopped.", "AutoFete");
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
            chat.PrintError("Enter the Firmament before recording.", "AutoFete");
            return;
        }

        recorder.Start();
        chat.Print("Recording FATEs, statuses, and nearby objects. Save from the debug window.", "AutoFete");
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
            chat.PrintError("Could not save capture. See /xllog.", "AutoFete");
        }
    }

    private sealed class RunnerWindow : Window
    {
        private static readonly Vector4 ActiveColor = new(0.36f, 0.87f, 0.48f, 1f);
        private static readonly Vector4 InactiveColor = new(0.96f, 0.32f, 0.32f, 1f);
        private readonly Plugin plugin;
        private readonly TitleBarButton runButton;

        public RunnerWindow(Plugin plugin) : base("AutoFete")
        {
            this.plugin = plugin;
            TitleBarButtons.Add(new TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                Click = _ => plugin.OpenSettingsWindow(),
                ShowTooltip = () =>
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Settings");
                    ImGui.EndTooltip();
                },
            });
            runButton = new TitleBarButton
            {
                Icon = FontAwesomeIcon.Play,
                Click = _ => plugin.OnCommand("/autofete", plugin.controller.IsArmed ? "stop" : "start"),
                ShowTooltip = () =>
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(plugin.controller.IsArmed ? "Disable" : "Enable");
                    ImGui.EndTooltip();
                },
            };
            TitleBarButtons.Add(runButton);
        }

        public override void PreDraw()
        {
            runButton.Icon = plugin.controller.IsArmed ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play;
            base.PreDraw();
        }

        public override void Draw()
        {
            if (plugin.controller.IsArmed)
            {
                ImGui.TextColored(ActiveColor, "Active");
                ImGui.TextColoredWrapped(ActiveColor, $"Stage: {plugin.ActiveStageLabel()}");
                ImGui.TextColoredWrapped(ActiveColor, plugin.controller.Status);
            }
            else
            {
                ImGui.TextColored(InactiveColor, "Inactive");
            }

            if (plugin.config.DebugEnabled)
            {
                ImGui.Separator();
                ImGui.Text("Fête waypoints (world coordinates):");
                foreach (var stageInfo in FeteCatalog.Stages)
                {
                    if (FeteCatalog.ProvisionalWaypoint(stageInfo.Stage) is { } waypoint)
                        ImGui.Text($"{stageInfo.Name}: {waypoint.X:F1}, {waypoint.Y:F1}, {waypoint.Z:F1}");
                    else
                        ImGui.Text($"{stageInfo.Name}: no waypoint");
                }
                if (!plugin.controller.IsArmed && ImGui.Button("Track only"))
                    plugin.controller.Start(plugin.config.Mode);
                if (ImGui.Button(plugin.recorder.IsRecording ? "Save capture" : "Record capture"))
                    plugin.ToggleRecording();
                ImGui.Text($"Recorded changes: {plugin.recorder.SampleCount}");
                if (plugin.controller.CurrentStage is { } stage)
                {
                    ImGui.Text($"{stage} target IDs:");
                    foreach (var role in FeteTargets.Roles[stage])
                    {
                        var hints = string.Join(", ", FeteTargets.Hints(stage, role)
                            .Select(x => $"{x.Sheet} #{x.RowId}"));
                        ImGui.TextUnformatted($"{role}: {hints}");
                    }
                }
            }
        }
    }

    private sealed class SettingsWindow(Plugin plugin) : Window("AutoFete Settings")
    {
        public override void Draw()
        {
            var mode = plugin.config.Mode == RunMode.RemainingCycle ? 1 : 0;
            ImGui.TextUnformatted("Completion mode");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##CompletionMode", ref mode,
                    "One cycle\0Continuous session\0"))
            {
                plugin.config.Mode = mode == 0 ? RunMode.OneSequence : RunMode.RemainingCycle;
                plugin.SaveConfig();
            }

            ImGui.Separator();

            var debugEnabled = plugin.config.DebugEnabled;
            if (ImGui.Checkbox("Enable debug tools", ref debugEnabled))
            {
                plugin.config.DebugEnabled = debugEnabled;
                if (!debugEnabled && plugin.recorder.IsRecording)
                    plugin.StopRecording();
                plugin.SaveConfig();
            }
        }
    }
}
