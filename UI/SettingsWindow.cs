using AutoFete;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoFete.UI;

internal sealed class SettingsWindow(Configuration config, CaptureRecorder recorder,
    Action saveConfig, Action stopRecording) : Window("AutoFete Settings")
{
    public override void Draw()
    {
        var mode = config.Mode == RunMode.RemainingCycle ? 1 : 0;
        ImGui.TextUnformatted("Completion mode");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##CompletionMode", ref mode,
                "One cycle\0Continuous session\0"))
        {
            config.Mode = mode == 0 ? RunMode.OneSequence : RunMode.RemainingCycle;
            saveConfig();
        }

        ImGui.Separator();

        var openPresents = config.OpenFetePresents;
        if (ImGui.Checkbox("Automatically open fete presents in inventory", ref openPresents))
        {
            config.OpenFetePresents = openPresents;
            saveConfig();
        }

        ImGui.Separator();

        var debugEnabled = config.DebugEnabled;
        if (ImGui.Checkbox("Enable debug tools", ref debugEnabled))
        {
            config.DebugEnabled = debugEnabled;
            if (!debugEnabled && recorder.IsRecording)
                stopRecording();
            saveConfig();
        }
    }
}
