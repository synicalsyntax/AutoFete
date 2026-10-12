using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class PresentsFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1716;
    public const string Name = "Presents of Mind";
    public const int GoldCount = 6;
    public static readonly Vector3 Waypoint = new(50.036926f, -19.95f, 37.29581f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["child-present"] = [new("EObjName", 2011676, "present for children")],
            ["merchant-present"] = [new("EObjName", 2011675, "present for merchants")],
            ["artisan-present"] = [new("EObjName", 2011529, "present for artisans")],
            ["firmament-boy"] = [new("BNpcName", 10092, "Firmament boy")],
            ["firmament-girl"] = [new("BNpcName", 10093, "Firmament girl")],
            ["foreign-merchant"] = [new("BNpcName", 10090, "foreign merchant")],
            ["ishgardian-merchant"] = [new("BNpcName", 10089, "Ishgardian merchant")],
            ["skysteel-engineer"] = [new("BNpcName", 10086, "Skysteel engineer")],
            ["dzemael-stonemason"] = [new("BNpcName", 10087, "House Dzemael stonemason")],
        };
    public static readonly FeteDefinition Definition = new(
        FeteStage.Presents, FateId, Name, GoldCount, Waypoint, Targets,
        WaitForJoinConfirmation: true, UsesHandinDialogs: true);

    private sealed record PresentRoute(string SourceRole, Vector3 SourcePosition,
        string DisplayNameKey, string[] RecipientRoles);

    private static readonly PresentRoute[] PresentRoutes =
    [
        new("child-present", new(42.01f, -18.9f, 29.41f), "Target_ChildrenPresent",
            ["firmament-boy", "firmament-girl"]),
        new("artisan-present", new(57.83f, -18.5f, 28.32f), "Target_ArtisanPresent",
            ["skysteel-engineer", "dzemael-stonemason"]),
        new("merchant-present", new(50.43f, -18.5f, 28.75f), "Target_MerchantPresent",
            ["foreign-merchant", "ishgardian-merchant"]),
    ];
    private static readonly Vector3 PresentDeliveryArea = new(48f, -20f, 75f);
    private Vector3? pickupRouteDestination;
    private string? pendingPresentRole;
    private string? carriedPresentRole;
    private DateTime waitingForPresentPickupUntilUtc;
    private DateTime waitingForPresentCreditUntilUtc;
    private bool awaitingPresentCredit;
    private int previousPresentsHandinCount = -1;

    protected override void ResetStage()
    {
        pendingPresentRole = null;
        carriedPresentRole = null;
        pickupRouteDestination = null;
        waitingForPresentPickupUntilUtc = DateTime.MinValue;
        waitingForPresentCreditUntilUtc = DateTime.MinValue;
        awaitingPresentCredit = false;
        previousPresentsHandinCount = -1;
    }

    public override string Tick(byte handInCount)
    {
        if (previousPresentsHandinCount >= 0 && handInCount > previousPresentsHandinCount)
        {
            pendingPresentRole = null;
            carriedPresentRole = null;
            awaitingPresentCredit = false;
            waitingForPresentCreditUntilUtc = DateTime.MinValue;
        }
        previousPresentsHandinCount = handInCount;

        if (handInCount >= GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return string.Empty;
        }

        var carryingPresent = actions.CarryingObject ||
                              objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 404) == true;
        if (carryingPresent)
        {
            pickupRouteDestination = null;
            carriedPresentRole ??= pendingPresentRole;
            pendingPresentRole = null;
            if (carriedPresentRole == null)
                return StatusText.Get("Error_UnidentifiedPresent");

            if (awaitingPresentCredit && DateTime.UtcNow < waitingForPresentCreditUntilUtc)
                return string.Empty;
            awaitingPresentCredit = false;

            var route = PresentRoutes.FirstOrDefault(x => x.SourceRole == carriedPresentRole);
            var recipient = FindClosestTarget(route?.RecipientRoles ?? []).Target;
            if (recipient == null)
            {
                if (MoveWithin(PresentDeliveryArea, 8f))
                    return string.Empty;
                return MissingTarget("recipient", StatusText.Format("Target_RecipientForPresent",
                    route == null ? carriedPresentRole : StatusText.Get(route.DisplayNameKey)));
            }

            actions.SelectTarget(recipient);
            if (MoveWithin(recipient.Position, 3f))
                return StatusText.Format("Progress_DeliveringTo", recipient.Name);
            if (TryInteract("present-delivery", recipient))
            {
                waitingForPresentCreditUntilUtc = DateTime.UtcNow.AddSeconds(8);
                awaitingPresentCredit = true;
            }
            return StatusText.Format("Progress_DeliveringTo", recipient.Name);
        }

        if (awaitingPresentCredit && DateTime.UtcNow < waitingForPresentCreditUntilUtc)
            return string.Empty;
        awaitingPresentCredit = false;
        carriedPresentRole = null;

        if (pendingPresentRole != null && DateTime.UtcNow < waitingForPresentPickupUntilUtc)
            return string.Empty;
        pendingPresentRole = null;

        var (role, present) = FindClosestTarget(PresentRoutes.Select(x => x.SourceRole));
        if (present == null)
        {
            var position = objects.LocalPlayer!.Position;
            var nextSource = PresentRoutes.MinBy(source =>
                Vector3.DistanceSquared(position, source.SourcePosition))!;
            if (MoveWithin(nextSource.SourcePosition, 3f, ref pickupRouteDestination))
                return string.Empty;
            return MissingTarget("present-source", StatusText.Get("Target_PresentSource"));
        }

        ClearMissingTarget();

        if (MoveWithin(present.Position, 3f, ref pickupRouteDestination))
            return string.Empty;
        if (TryInteract("present-pickup", present))
        {
            waitingForPresentPickupUntilUtc = DateTime.UtcNow.AddSeconds(10);
            pendingPresentRole = role;
        }
        return StatusText.Format("Progress_PickingUp", present.Name);
    }

    private (string? Role, IGameObject? Target) FindClosestTarget(IEnumerable<string> roles)
    {
        string? bestRole = null;
        IGameObject? bestTarget = null;
        var position = objects.LocalPlayer?.Position ?? Vector3.Zero;
        foreach (var role in roles)
        {
            var target = FindTarget(role);
            if (target != null && (bestTarget == null ||
                Vector3.DistanceSquared(position, target.Position) <
                Vector3.DistanceSquared(position, bestTarget.Position)))
            {
                bestRole = role;
                bestTarget = target;
            }
        }
        return (bestRole, bestTarget);
    }
}
