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
        ImGui.TextWrapped("One cycle disables automation after the final Fête in a cycle. Continuous session keeps automation enabled through later cycles until stopped.");

        ImGui.Separator();

        var openPresents = config.OpenFetePresents;
        if (ImGui.Checkbox("Automatically open Fête presents in inventory", ref openPresents))
        {
            config.OpenFetePresents = openPresents;
            saveConfig();
        }

        var continueActions = config.ContinueFeteActionsAfterFullCredit;
        if (ImGui.Checkbox("Continue Fête actions after earning full credit", ref continueActions))
        {
            config.ContinueFeteActionsAfterFullCredit = continueActions;
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
