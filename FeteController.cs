using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Fates;
using System.Numerics;

namespace AutoFete;

internal sealed class FeteController(IClientState clientState, IObjectTable objects, IFateTable fates,
    Navmesh navmesh, GameActions actions, FeteActionAutomation automation)
{
    private ushort? currentFateId;
    private FeteStage? currentStage;
    private FeteStage? lastCompletedStage;
    private Vector3? routeDestination;
    private bool sawPresents;
    private bool autoJoin;
    private bool joinedCurrentStage;

    public bool IsArmed { get; private set; }
    public string Status { get; private set; } = "Stopped";
    public RunMode Mode { get; private set; }
    public FeteStage? CurrentStage => currentStage;
    public bool WantsHandinDialogs => autoJoin && currentFateId is { } id &&
                                       (currentStage is FeteStage.SofterStuff or FeteStage.ShearYak or
                                           FeteStage.ToyHunter or FeteStage.Presents) &&
                                      (currentStage == FeteStage.SofterStuff || actions.IsJoined(id));

    public bool Start(RunMode mode, bool join = false)
    {
        if (!clientState.IsLoggedIn || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
        {
            Status = "Enter the Firmament before starting.";
            return false;
        }

        var job = objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (job is < 8 or > 18)
        {
            Status = "Switch to a Disciple of the Hand or Land.";
            return false;
        }

        if (!navmesh.IsReady())
        {
            Status = "vnavmesh is missing or its navigation mesh is not ready.";
            return false;
        }

        Mode = mode;
        autoJoin = join;
        IsArmed = true;
        currentFateId = null;
        currentStage = null;
        lastCompletedStage = null;
        routeDestination = null;
        sawPresents = false;
        joinedCurrentStage = false;
        automation.Reset();
        Status = join ? "Waiting to join a Fête." : "Tracking Fêtes.";
        return true;
    }

    public void Stop(string reason = "Stopped")
    {
        IsArmed = false;
        autoJoin = false;
        currentFateId = null;
        currentStage = null;
        lastCompletedStage = null;
        routeDestination = null;
        joinedCurrentStage = false;
        automation.Reset();
        navmesh.Stop();
        Status = reason;
    }

    public void Tick()
    {
        if (!IsArmed)
            return;

        if (!clientState.IsLoggedIn || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
        {
            Stop("Stopped: left the Firmament or logged out.");
            return;
        }

        if (!navmesh.IsReady())
        {
            Stop("Stopped: vnavmesh became unavailable.");
            return;
        }

        var job = objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (job is < 8 or > 18)
        {
            Stop("Stopped: current job is not a crafter or gatherer.");
            return;
        }

        var visible = fates
            .Select(f => (Fate: f, Stage: FeteCatalog.Find(f.FateId)))
            .Where(x => x.Stage != null &&
                        x.Fate.State is FateState.Preparing or FateState.Running or FateState.Ending)
            .OrderBy(x => Array.FindIndex(FeteCatalog.Stages, s => s.Stage == x.Stage!.Value.Stage))
            .ToArray();

        var active = visible.FirstOrDefault(x => x.Fate.State == FateState.Running);
        if (active.Fate == null)
            active = visible.FirstOrDefault(x => x.Fate.State == FateState.Ending);

        if (active.Fate == null)
        {
            if (sawPresents && Mode == RunMode.OneSequence)
            {
                Stop("Sequence ended.");
                return;
            }

            if (currentStage != null)
            {
                lastCompletedStage = currentStage;
                currentStage = null;
                currentFateId = null;
                joinedCurrentStage = false;
                automation.Reset();
                navmesh.Stop();
                routeDestination = null;
            }

            if (!autoJoin)
            {
                Status = "Waiting for the next Fête.";
                return;
            }

            var preparing = visible.FirstOrDefault(x => x.Fate.State == FateState.Preparing);
            if (preparing.Fate != null)
            {
                if (TryGetStageApproach(preparing.Stage!.Value.Stage, out var approach))
                    MoveToward(preparing.Stage!.Value.Name, approach.Position, approach.Radius,
                        "preparing Fête");
                return;
            }

            if (lastCompletedStage is { } previous)
            {
                var nextStage = FeteCatalog.Next(previous);
                var name = FeteCatalog.Stages.First(x => x.Stage == nextStage).Name;
                if (TryGetStageApproach(nextStage, out var next))
                {
                    MoveToward(name, next.Position, next.Radius, "Fête location");
                    return;
                }
            }

            Status = "Waiting for the next Fête location.";
            return;
        }

        if (active.Fate.FateId != currentFateId)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            routeDestination = null;
            currentFateId = active.Fate.FateId;
            currentStage = active.Stage!.Value.Stage;
            lastCompletedStage = null;
            joinedCurrentStage = false;
            automation.Reset();
            if (currentStage == FeteStage.Presents)
                sawPresents = true;
        }

        if (!autoJoin)
        {
            Status = $"Tracking {active.Stage!.Value.Name} (FATE {currentFateId}).";
            return;
        }

        var playerPosition = objects.LocalPlayer!.Position;
        if (actions.IsJoined(active.Fate.FateId))
        {
            if (!joinedCurrentStage)
            {
                if (navmesh.IsMoving()) navmesh.Stop();
                routeDestination = null;
                joinedCurrentStage = true;
            }
            Status = automation.Tick(currentStage!.Value, active.Fate.FateId, active.Fate.HandInCount);
            return;
        }

        if (currentStage == FeteStage.SofterStuff && joinedCurrentStage)
        {
            Status = automation.Tick(currentStage.Value, active.Fate.FateId, active.Fate.HandInCount);
            return;
        }

        joinedCurrentStage = false;

        if (!TryGetStageApproach(currentStage!.Value, out var joinApproach))
        {
            Status = "No waypoint is configured for this Fête stage.";
            return;
        }
        if (Vector3.Distance(playerPosition, joinApproach.Position) > Math.Max(2f, joinApproach.Radius - 3f))
        {
            MoveToward(active.Stage!.Value.Name, joinApproach.Position, joinApproach.Radius,
                "join location");
            return;
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        actions.TryJoin(active.Fate.FateId, playerPosition, active.Fate.Position, active.Fate.Radius);
        if (currentStage == FeteStage.SofterStuff)
        {
            // Materials and toy interactions can begin without waiting for the
            // FATE participant status to appear after the join request.
            joinedCurrentStage = true;
            Status = automation.Tick(currentStage.Value, active.Fate.FateId, active.Fate.HandInCount);
            return;
        }
        Status = $"Joining the Fête: {actions.LastJoinAttempt} Waiting for game confirmation.";
    }

    private static bool TryGetStageApproach(FeteStage stage, out (Vector3 Position, float Radius) approach)
    {
        if (FeteCatalog.ProvisionalWaypoint(stage) is { } waypoint)
        {
            approach = (waypoint, 6f);
            return true;
        }

        approach = default;
        return false;
    }

    private void MoveToward(string name, Vector3 position, float radius, string source)
    {
        if (!IsUsablePosition(position))
        {
            Status = $"Waiting for {name} to reveal its location.";
            return;
        }

        var distance = Math.Max(2f, radius - 3f);
        if (routeDestination is { } oldDestination &&
            Vector3.DistanceSquared(oldDestination, position) > 4f)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            routeDestination = null;
        }

        if (Vector3.Distance(objects.LocalPlayer!.Position, position) <= distance)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            routeDestination = null;
            Status = $"At {name}; waiting for it to run.";
            return;
        }

        if (!navmesh.IsMoving())
        {
            if (!navmesh.MoveNear(position, distance))
            {
                Status = $"Could not path to {name} ({source}).";
                return;
            }
            routeDestination = position;
        }

        Status = $"Moving to {name} ({source}).";
    }

    private static bool IsUsablePosition(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        position.LengthSquared() > 1f;
}
