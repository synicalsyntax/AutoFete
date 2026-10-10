using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace AutoFete;

internal sealed class HandinDialogs : IDisposable
{
    private const string Owner = "AutoFete";
    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> enable;
    private readonly ICallGateSubscriber<string, bool> disable;
    private bool enabled;
    private DateTime nextAttemptUtc;

    public HandinDialogs(IDalamudPluginInterface pluginInterface)
    {
        enable = pluginInterface.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>(
            "TextAdvance.EnableExternalControl");
        disable = pluginInterface.GetIpcSubscriber<string, bool>(
            "TextAdvance.DisableExternalControl");
    }

    public void Tick(bool active)
    {
        if (!active)
        {
            Disable();
            return;
        }

        if (enabled || DateTime.UtcNow < nextAttemptUtc)
            return;

        nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
        try
        {
            if (enable.HasFunction)
                enabled = enable.InvokeFunc(Owner, new ExternalTerritoryConfig
                {
                    EnableTalkSkip = true,
                    EnableRequestFill = true,
                    EnableRequestHandin = true,
                });
        }
        catch { }
    }

    public void Disable()
    {
        if (!enabled)
            return;
        try { disable.InvokeFunc(Owner); }
        catch { }
        enabled = false;
    }

    public void Dispose() => Disable();

    public sealed class ExternalTerritoryConfig
    {
        public bool? EnableQuestAccept;
        public bool? EnableQuestComplete;
        public bool? EnableRewardPick;
        public bool? EnableRequestHandin;
        public bool? EnableCutsceneEsc;
        public bool? EnableCutsceneSkipConfirm;
        public bool? EnableTalkSkip;
        public bool? EnableRequestFill;
        public bool? EnableAutoInteract;
    }
}
