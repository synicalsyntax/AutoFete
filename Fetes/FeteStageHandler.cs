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
    private readonly Dictionary<string, DateTime> nextInteractionUtc = [];
    private string? missingTargetRole;
    private DateTime missingTargetSinceUtc;

    public bool IsComplete { get; private set; }
    public int? FullCreditPoints { get; private set; }

    public void Reset()
    {
        IsComplete = false;
        FullCreditPoints = null;
        ClearMissingTarget();
        nextInteractionUtc.Clear();
        ResetStage();
    }

    protected abstract void ResetStage();
    public abstract string? Tick(byte handInCount, bool continueAfterFullCredit);

    protected bool ShouldComplete(int earned, bool continueAfterFullCredit)
    {
        if (earned >= definition.MinPointCount)
            FullCreditPoints ??= earned;
        if (continueAfterFullCredit)
            IsComplete = false;
        return !continueAfterFullCredit && earned >= definition.MinPointCount;
    }

    protected string CompleteStage(int earned)
    {
        IsComplete = true;
        if (navmesh.IsMoving()) navmesh.Stop();
        return StatusText.Format("Result_StageAutomationComplete", definition.Name, earned);
    }

    protected IGameObject? FindTarget(string role)
    {
        var target = actions.FindTarget(definition, role);
        if (target != null)
            ClearMissingTarget();
        return target;
    }

    protected string TargetName(string role)
    {
        var visible = objects.FirstOrDefault(x => Matches(role, x));
        if (visible != null)
            return visible.Name.ToString();
        return definition.Targets.TryGetValue(role, out var hints) && hints.Length > 0
            ? hints[0].Name
            : role;
    }

    protected string MissingTarget(string role, string name)
    {
        var now = DateTime.UtcNow;
        if (missingTargetRole != role)
        {
            missingTargetRole = role;
            missingTargetSinceUtc = now;
        }
        return now - missingTargetSinceUtc >= TimeSpan.FromSeconds(8)
            ? StatusText.Format("Error_CannotFindTarget", name)
            : string.Empty;
    }

    protected void ClearMissingTarget()
    {
        missingTargetRole = null;
        missingTargetSinceUtc = DateTime.MinValue;
    }

    protected bool Matches(string role, IGameObject target) =>
        FeteTargets.Matches(definition, role, target);

    protected bool TryInteract(string role, IGameObject target)
    {
        var now = DateTime.UtcNow;
        if (nextInteractionUtc.TryGetValue(role, out var nextAttempt) && now < nextAttempt)
            return false;

        var player = objects.LocalPlayer;
        if (player == null || !actions.TryInteract(target, player.Position))
            return false;

        nextInteractionUtc[role] = DateTime.UtcNow.AddSeconds(2);
        return true;
    }

    protected bool MoveWithin(Vector3 position, float range)
    {
        Vector3? routeDestination = null;
        return MoveWithin(position, range, ref routeDestination);
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
