using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal abstract class FeteStageHandler(FeteDefinition definition, GameActions actions,
    Navmesh navmesh, IObjectTable objects)
{
    private readonly FeteDefinition definition = definition;
    protected readonly GameActions actions = actions;
    protected readonly Navmesh navmesh = navmesh;
    protected readonly IObjectTable objects = objects;

    public abstract void Reset();
    public abstract string? Tick(byte handInCount);

    protected IGameObject? FindTarget(string role) => actions.FindTarget(definition, role);

    protected bool Matches(string role, IGameObject target) =>
        FeteTargets.Matches(definition, role, target);

    protected bool MoveWithin(Vector3 position, float range)
    {
        var player = objects.LocalPlayer;
        if (player == null || Vector3.Distance(player.Position, position) <= range)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return false;
        }

        if (!navmesh.IsMoving()) navmesh.MoveNear(position, Math.Max(1f, range - 1f));
        return true;
    }

    // Repath when the desired object changes while a route is already running.
    protected bool MoveWithin(Vector3 position, float range, ref Vector3? routeDestination)
    {
        var player = objects.LocalPlayer;
        if (player == null || Vector3.Distance(player.Position, position) <= range)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            routeDestination = null;
            return false;
        }

        if (routeDestination is { } previous &&
            Vector3.DistanceSquared(previous, position) > 4f && navmesh.IsMoving())
            navmesh.Stop();
        if (!navmesh.IsMoving() &&
            navmesh.MoveNear(position, Math.Max(1f, range - 1f)))
            routeDestination = position;
        return true;
    }
}
