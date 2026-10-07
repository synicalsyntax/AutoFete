using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Numerics;

namespace AutoFete;

internal sealed class Navmesh(IDalamudPluginInterface pluginInterface)
{
    private readonly ICallGateSubscriber<bool> ready =
        pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> move =
        pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
    private readonly ICallGateSubscriber<object> stop =
        pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    private readonly ICallGateSubscriber<bool> running =
        pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");

    public bool IsReady()
    {
        try { return ready.InvokeFunc(); }
        catch { return false; }
    }

    public bool IsMoving()
    {
        try { return running.InvokeFunc(); }
        catch { return false; }
    }

    public bool MoveNear(Vector3 position, float distance = 3f)
    {
        try { return IsReady() && move.InvokeFunc(position, false, distance); }
        catch { return false; }
    }

    public void Stop()
    {
        try { stop.InvokeAction(); }
        catch { /* vnavmesh may already have unloaded. */ }
    }
}
