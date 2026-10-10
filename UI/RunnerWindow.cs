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
                ImGui.TextUnformatted(controller.IsArmed ? "Disable" : "Enable");
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
            ImGui.TextColored(ActiveColor, "Active");
            ImGui.TextUnformatted("Stage:");
            ImGui.SameLine();
            ImGui.TextColoredWrapped(StageColor, activeStageLabel());
            ImGui.TextColoredWrapped(StageColor, controller.Status);
        }
        else
        {
            ImGui.TextColored(InactiveColor, "Inactive");
        }

        if (config.DebugEnabled)
        {
            ImGui.Separator();
            ImGui.Text("Fête waypoints (world coordinates):");
            foreach (var stageInfo in FeteCatalog.Stages)
            {
                var waypoint = stageInfo.Waypoint;
                ImGui.Text($"{stageInfo.Name}: {waypoint.X:F1}, {waypoint.Y:F1}, {waypoint.Z:F1}");
            }
            if (!controller.IsArmed && ImGui.Button("Track only"))
                controller.Start(config.Mode);
            if (ImGui.Button(recorder.IsRecording ? "Save capture" : "Record capture"))
                toggleRecording();
            ImGui.Text($"Recorded changes: {recorder.SampleCount}");
            if (controller.CurrentStage is { } stage)
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
