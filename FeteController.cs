using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Fates;
using System.Numerics;

namespace AutoFete;

internal sealed class FeteController(IClientState clientState, IObjectTable objects, IFateTable fates,
    Navmesh navmesh, GameActions actions, FeteActionAutomation automation, Configuration config,
    Action saveConfig)
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
                                           FeteStage.ToyHunter or FeteStage.Presents) && actions.IsJoined(id);

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
            .Where(x => x.Stage != null && x.Fate.State is FateState.Preparing or FateState.Running)
            .OrderBy(x => Array.FindIndex(FeteCatalog.Stages, s => s.Stage == x.Stage!.Value.Stage))
            .ToArray();

        foreach (var fete in visible)
            RememberLocation(fete.Stage!.Value.Stage, fete.Fate.Position, fete.Fate.Radius);

        var active = visible.FirstOrDefault(x => x.Fate.State == FateState.Running);

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
                MoveToward(preparing.Stage!.Value.Name, preparing.Fate.Position, preparing.Fate.Radius,
                    "preparing Fête");
                return;
            }

            if (lastCompletedStage is { } previous)
            {
                var nextStage = FeteCatalog.Next(previous);
                var name = FeteCatalog.Stages.First(x => x.Stage == nextStage).Name;
                if (config.StageLocations != null &&
                    config.StageLocations.TryGetValue(nextStage.ToString(), out var next))
                {
                    MoveToward(name, new Vector3(next.X, next.Y, next.Z), next.Radius,
                        "observed Fête location");
                    return;
                }

                if (FeteCatalog.ProvisionalWaypoint(nextStage) is { } waypoint)
                {
                    MoveToward(name, waypoint, 3f, "provisional waypoint");
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
            Status = automation.Tick(currentStage!.Value, active.Fate.FateId);
            return;
        }

        joinedCurrentStage = false;

        var joinDistance = Math.Max(2f, active.Fate.Radius - 3f);
        if (Vector3.Distance(playerPosition, active.Fate.Position) > joinDistance)
        {
            if (!navmesh.IsMoving()) navmesh.MoveNear(active.Fate.Position, joinDistance);
            Status = "Moving into the Fête area.";
            return;
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        actions.TryJoin(active.Fate.FateId, playerPosition, active.Fate.Position, active.Fate.Radius);
        Status = "Joining the Fête; waiting for game confirmation.";
    }

    private void RememberLocation(FeteStage stage, Vector3 position, float radius)
    {
        if (!IsUsablePosition(position))
            return;

        config.StageLocations ??= [];
        var key = stage.ToString();
        var savedRadius = float.IsFinite(radius) ? Math.Max(radius, 3f) : 3f;
        if (config.StageLocations.TryGetValue(key, out var old) &&
            Vector3.DistanceSquared(new Vector3(old.X, old.Y, old.Z), position) < 4f &&
            Math.Abs(old.Radius - savedRadius) < 2f)
            return;

        config.StageLocations[key] = new StageLocation
        {
            X = position.X, Y = position.Y, Z = position.Z, Radius = savedRadius,
        };
        saveConfig();
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
