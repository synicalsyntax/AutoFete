using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Fates;
using System.Numerics;

namespace AutoFete;

internal sealed class FeteController(IClientState clientState, IObjectTable objects, IFateTable fates,
    Navmesh navmesh, GameActions actions, FeteActionAutomation automation,
    HandinDialogs handinDialogs, Configuration config)
{
    private const float StageApproachRadius = 6f;
    private readonly FetePresentOpener presentOpener = new(actions);
    private ushort? currentFateId;
    private FeteStage? currentStage;
    private FeteStage? lastCompletedStage;
    private Vector3? routeDestination;
    private bool sawFinalStage;
    private bool processingCycleRewards;
    private bool autoJoin;
    private bool joinedCurrentStage;

    public event Action<string>? StageCompleted;
    public event Action<string>? AutomationFailed;

    public bool IsArmed { get; private set; }
    public string Status { get; private set; } = StatusText.Get("Stop_Default");
    public RunMode Mode { get; private set; }
    public FeteStage? CurrentStage => currentStage;
    public bool IsAutomatingStage => IsArmed && autoJoin && currentFateId is { } id &&
                                     !automation.IsComplete(id);
    public bool WantsHandinDialogs => autoJoin && currentFateId is { } id &&
                                       FeteCatalog.Find(id) is { } stage &&
                                       stage.UsesHandinDialogs &&
                                       (!stage.WaitForJoinConfirmation || actions.IsJoined(id));

    public bool Start(RunMode mode, bool join = false)
    {
        if (!clientState.IsLoggedIn || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
        {
            Status = StatusText.Get("Error_EnterFirmament");
            return false;
        }

        var job = objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (job is < 8 or > 18)
        {
            Status = StatusText.Get("Error_ChangeToCrafterOrGatherer");
            return false;
        }

        if (!navmesh.IsReady())
        {
            Status = StatusText.Get("Error_NavmeshUnavailable");
            return false;
        }

        if (!handinDialogs.IsAvailable)
        {
            Status = StatusText.Get("Error_TextAdvanceUnavailable");
            return false;
        }

        Mode = mode;
        autoJoin = join;
        IsArmed = true;
        ResetSessionState();
        actions.ResetJoinStatus();
        Status = string.Empty;
        return true;
    }

    public void Stop(string? reason = null)
    {
        IsArmed = false;
        autoJoin = false;
        ResetSessionState();
        navmesh.Stop();
        Status = reason ?? StatusText.Get("Stop_Default");
    }

    public void StopWithError(string reason)
    {
        var wasArmed = IsArmed;
        Stop(reason);
        if (wasArmed)
            AutomationFailed?.Invoke(reason);
    }

    public void Tick()
    {
        if (!IsArmed)
            return;

        if (!clientState.IsLoggedIn || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
        {
            StopWithError(StatusText.Get("Stop_LeftFirmamentOrLoggedOut"));
            return;
        }

        if (!navmesh.IsReady())
        {
            StopWithError(StatusText.Get("Stop_NavmeshUnavailable"));
            return;
        }

        if (!handinDialogs.IsAvailable)
        {
            StopWithError(StatusText.Get("Stop_TextAdvanceUnavailable"));
            return;
        }

        var job = objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (job is < 8 or > 18)
        {
            StopWithError(StatusText.Get("Stop_InvalidJob"));
            return;
        }

        var visible = FeteCatalog.WithDefinitions(fates)
            .Where(x => x.Stage != null &&
                        x.Fate.State is FateState.Preparing or FateState.Running or FateState.Ending)
            .OrderBy(x => Array.FindIndex(FeteCatalog.Stages, s => s.Stage == x.Stage!.Value.Stage))
            .ToArray();

        var active = visible.FirstOrDefault(x => x.Fate.State == FateState.Running);
        if (active.Fate == null)
            active = visible.FirstOrDefault(x => x.Fate.State == FateState.Ending);

        if (active.Fate == null)
        {
            if (currentStage != null)
            {
                lastCompletedStage = currentStage;
                currentStage = null;
                currentFateId = null;
                joinedCurrentStage = false;
                automation.Reset();
                StopRoute();
            }

            if (sawFinalStage)
            {
                sawFinalStage = false;
                processingCycleRewards = true;
                if (autoJoin && config.OpenFetePresents)
                    presentOpener.Start(waitForRewards: true);
            }

            if (!config.OpenFetePresents)
                presentOpener.Reset();
            else if (autoJoin && !presentOpener.IsActive && !processingCycleRewards &&
                     presentOpener.ShouldStart(GameActions.GetInventoryItemCount(FetePresentOpener.PresentItemId)))
                presentOpener.Start();

            if (presentOpener.IsActive && !presentOpener.Tick())
            {
                StopRoute();
                Status = presentOpener.Status;
                return;
            }

            if (processingCycleRewards)
            {
                processingCycleRewards = false;
                if (Mode == RunMode.OneSequence)
                {
                    var lastStatus = presentOpener.Status.Length > 0 ? presentOpener.Status : Status;
                    var stopped = StatusText.Get("Stop_SingleCycleComplete");
                    Stop(string.IsNullOrWhiteSpace(lastStatus)
                        ? stopped
                        : $"{lastStatus}\n{stopped}");
                    return;
                }
            }

            if (!autoJoin)
            {
                Status = string.Empty;
                return;
            }

            var preparing = visible.FirstOrDefault(x => x.Fate.State == FateState.Preparing);
            if (preparing.Fate != null)
            {
                var stage = preparing.Stage!.Value;
                MoveToward(stage.Name, stage.Waypoint, StageApproachRadius);
                return;
            }

            if (lastCompletedStage is { } previous)
            {
                var next = FeteCatalog.Find(FeteCatalog.Next(previous))!.Value;
                MoveToward(next.Name, next.Waypoint, StageApproachRadius);
                return;
            }

            Status = string.Empty;
            return;
        }

        if (presentOpener.IsActive)
            presentOpener.Reset();

        if (active.Fate.FateId != currentFateId)
            BeginStage(active.Fate.FateId, active.Stage!.Value.Stage);

        if (!autoJoin)
        {
            Status = string.Empty;
            return;
        }

        var playerPosition = objects.LocalPlayer!.Position;
        if (actions.IsJoined(active.Fate.FateId))
        {
            if (!joinedCurrentStage)
            {
                StopRoute();
                joinedCurrentStage = true;
            }
            RunStage(active.Fate.FateId, active.Fate.HandInCount);
            return;
        }

        if (!active.Stage!.Value.WaitForJoinConfirmation && joinedCurrentStage)
        {
            RunStage(active.Fate.FateId, active.Fate.HandInCount);
            return;
        }

        joinedCurrentStage = false;

        var joinWaypoint = active.Stage!.Value.Waypoint;
        if (Vector3.Distance(playerPosition, joinWaypoint) > Math.Max(2f, StageApproachRadius - 3f))
        {
            MoveToward(active.Stage!.Value.Name, joinWaypoint, StageApproachRadius);
            return;
        }

        StopRoute();
        actions.TryJoin(active.Fate.FateId, playerPosition, active.Fate.Position, active.Fate.Radius);
        if (!active.Stage!.Value.WaitForJoinConfirmation)
        {
            // Materials and toy interactions can begin without waiting for the
            // FATE participant status to appear after the join request.
            joinedCurrentStage = true;
            RunStage(active.Fate.FateId, active.Fate.HandInCount);
            return;
        }
        Status = actions.LastJoinError ?? string.Empty;
    }

    private void ResetSessionState()
    {
        currentFateId = null;
        currentStage = null;
        lastCompletedStage = null;
        routeDestination = null;
        joinedCurrentStage = false;
        sawFinalStage = false;
        processingCycleRewards = false;
        presentOpener.Reset();
        automation.Reset();
    }

    private void BeginStage(ushort fateId, FeteStage stage)
    {
        StopRoute();
        currentFateId = fateId;
        currentStage = stage;
        lastCompletedStage = null;
        joinedCurrentStage = false;
        actions.ResetJoinStatus();
        automation.Reset();
        if (stage == FeteCatalog.Stages[^1].Stage)
            sawFinalStage = true;
    }

    private void RunStage(ushort fateId, byte handInCount)
    {
        var hadFullCredit = automation.FullCreditPoints(fateId) != null;
        Status = automation.Tick(fateId, handInCount, config.ContinueFeteActionsAfterFullCredit) ?? Status;
        if (!hadFullCredit && automation.FullCreditPoints(fateId) is { } points &&
            FeteCatalog.Find(fateId) is { } stage)
            StageCompleted?.Invoke(StatusText.Format("Result_StageAutomationComplete", stage.Name, points));
    }

    private void StopRoute()
    {
        if (navmesh.IsMoving()) navmesh.Stop();
        routeDestination = null;
    }

    private void MoveToward(string name, Vector3 position, float radius)
    {
        if (!IsUsablePosition(position))
        {
            Status = StatusText.Format("Error_WaypointUnavailable", name);
            return;
        }

        var distance = Math.Max(2f, radius - 3f);
        if (routeDestination is { } oldDestination &&
            Vector3.DistanceSquared(oldDestination, position) > 4f)
            StopRoute();

        if (Vector3.Distance(objects.LocalPlayer!.Position, position) <= distance)
        {
            StopRoute();
            Status = string.Empty;
            return;
        }

        if (!navmesh.IsMoving())
        {
            if (!navmesh.MoveNear(position, distance))
            {
                Status = StatusText.Format("Error_PathUnavailable", name);
                return;
            }
            routeDestination = position;
        }

        Status = StatusText.Format("Progress_MovingTo", name);
    }

    private static bool IsUsablePosition(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        position.LengthSquared() > 1f;
}
