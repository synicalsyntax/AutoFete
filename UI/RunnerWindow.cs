using AutoFete;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFete.UI;

internal sealed class RunnerWindow : Window
{
    private static readonly Vector4 ActiveColor = new(0.36f, 0.87f, 0.48f, 1f);
    private static readonly Vector4 InactiveColor = new(0.96f, 0.32f, 0.32f, 1f);
    private static readonly Vector4 StageColor = new(0.25f, 0.9f, 0.9f, 1f);
    private readonly FeteController controller;
    private readonly CaptureRecorder recorder;
    private readonly Configuration config;
    private readonly Func<string> activeStageLabel;
    private readonly Action toggleRecording;
    private readonly TitleBarButton runButton;

    public RunnerWindow(FeteController controller, CaptureRecorder recorder, Configuration config,
        Func<string> activeStageLabel, Action openSettings, Action toggleRecording, Action toggleRun)
        : base("AutoFete")
    {
        this.controller = controller;
        this.recorder = recorder;
        this.config = config;
        this.activeStageLabel = activeStageLabel;
        this.toggleRecording = toggleRecording;
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            Click = _ => openSettings(),
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
            Click = _ => toggleRun(),
            ShowTooltip = () =>
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(controller.IsArmed ? "Stop automation" : "Start automation");
                ImGui.EndTooltip();
            },
        };
        TitleBarButtons.Add(runButton);
    }

    public override void PreDraw()
    {
        runButton.Icon = controller.IsArmed ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play;
        base.PreDraw();
    }

    public override void Draw()
    {
        if (controller.IsArmed)
        {
            var stage = activeStageLabel();
            ImGui.TextColored(ActiveColor, StatusText.Get("Ui_Active"));
            ImGui.TextUnformatted(StatusText.Get("Ui_StageLabel"));
            ImGui.SameLine(0, 0);
            ImGui.TextColoredWrapped(StageColor, $" {stage}");
            if (!string.IsNullOrWhiteSpace(controller.Status))
                ImGui.TextWrapped(controller.Status);
            else if (stage == StatusText.Get("Ui_NoStage"))
                ImGui.TextWrapped(StatusText.Get("Ui_WaitingForFete"));
        }
        else
        {
            ImGui.TextColored(InactiveColor, StatusText.Get("Ui_Inactive"));
            if (!string.IsNullOrWhiteSpace(controller.Status) &&
                controller.Status != StatusText.Get("Stop_Default"))
                ImGui.TextWrapped(controller.Status);
        }

        if (config.DebugEnabled)
        {
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Debug tools", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextColored(StageColor, "Game state capture");
                if (ImGui.Button(recorder.IsRecording ? "Save capture" : "Start capture"))
                    toggleRecording();
                ImGui.SameLine();
                ImGui.Text($"Snapshots recorded: {recorder.SampleCount}");
                ImGui.TextWrapped("Records FATE state, statuses, targets, and nearby objects during automated or manual play.");
                ImGui.TextWrapped("Save capture writes a JSONL file to the plugin configuration folder for future analysis.");
                if (controller.CurrentStage is { } stage)
                {
                    ImGui.Separator();
                    ImGui.TextColored(StageColor, "Current stage target IDs");
                    ImGui.TextUnformatted(stage.ToString());
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
}
