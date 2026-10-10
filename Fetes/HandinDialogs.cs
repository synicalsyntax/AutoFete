using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace AutoFete;

// TextAdvance's external-control contract, as used by Questionable and CBT.
// Only Request fill/hand-in and the accompanying Talk skip are enabled.
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
        catch { /* TextAdvance is optional and can unload during a Fête. */ }
    }

    public void Disable()
    {
        if (!enabled)
            return;
        try { disable.InvokeFunc(Owner); }
        catch { /* TextAdvance may already have unloaded. */ }
        enabled = false;
    }

    public void Dispose() => Disable();

    // Keep field names and types identical to TextAdvance's external IPC data.
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
