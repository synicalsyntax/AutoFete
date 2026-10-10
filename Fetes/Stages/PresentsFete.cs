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

    private static readonly string[] PresentSourceRoles =
        ["child-present", "artisan-present", "merchant-present"];
    private static readonly string[] ChildRecipientRoles = ["firmament-boy", "firmament-girl"];
    private static readonly string[] ArtisanRecipientRoles =
        ["skysteel-engineer", "dzemael-stonemason"];
    private static readonly string[] MerchantRecipientRoles =
        ["foreign-merchant", "ishgardian-merchant"];
    private static readonly (string Role, Vector3 Position)[] PresentSources =
    [
        ("child-present", new Vector3(42.01f, -18.9f, 29.41f)),
        ("merchant-present", new Vector3(50.43f, -18.5f, 28.75f)),
        ("artisan-present", new Vector3(57.83f, -18.5f, 28.32f)),
    ];
    private static readonly Vector3 PresentDeliveryArea = new(48f, -20f, 75f);
    private Vector3? pickupRouteDestination;
    private string? pendingPresentRole;
    private string? carriedPresentRole;
    private DateTime nextPresentPickupUtc;
    private DateTime nextPresentDeliveryUtc;
    private DateTime waitingForPresentPickupUntilUtc;
    private DateTime waitingForPresentCreditUntilUtc;
    private bool awaitingPresentCredit;
    private int previousPresentsHandinCount = -1;

    public override void Reset()
    {
        pendingPresentRole = null;
        carriedPresentRole = null;
        pickupRouteDestination = null;
        nextPresentPickupUtc = DateTime.MinValue;
        nextPresentDeliveryUtc = DateTime.MinValue;
        waitingForPresentPickupUntilUtc = DateTime.MinValue;
        waitingForPresentCreditUntilUtc = DateTime.MinValue;
        awaitingPresentCredit = false;
        previousPresentsHandinCount = -1;
    }

    public override string Tick(byte handInCount) => TickPresents(handInCount);

    private string TickPresents(byte handInCount)
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
            return "Six presents delivered; waiting for the Fête to finish.";
        }

        var carryingPresent = actions.CarryingObject ||
                              objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 404) == true;
        if (carryingPresent)
        {
            pickupRouteDestination = null;
            carriedPresentRole ??= pendingPresentRole;
            pendingPresentRole = null;
            if (carriedPresentRole == null)
                return "Carrying an unidentified present; finish this delivery manually.";

            if (awaitingPresentCredit && DateTime.UtcNow < waitingForPresentCreditUntilUtc)
                return "Waiting for the present delivery to complete.";
            awaitingPresentCredit = false;

            var recipient = FindClosestPresentTarget(RecipientRoles(carriedPresentRole));
            if (recipient == null)
            {
                if (MoveWithin(PresentDeliveryArea, 8f))
                    return $"Moving toward {carriedPresentRole} recipients.";
                return $"Looking for an available {carriedPresentRole} recipient.";
            }

            actions.SelectTarget(recipient);
            if (MoveWithin(recipient.Position, 3f))
                return $"Carrying {carriedPresentRole} to {recipient.Name}.";
            if (DateTime.UtcNow >= nextPresentDeliveryUtc &&
                actions.TryInteract(recipient, objects.LocalPlayer!.Position))
            {
                nextPresentDeliveryUtc = DateTime.UtcNow.AddSeconds(2);
                waitingForPresentCreditUntilUtc = DateTime.UtcNow.AddSeconds(8);
                awaitingPresentCredit = true;
            }
            return $"Delivering {carriedPresentRole} to {recipient.Name} ({handInCount}/6).";
        }

        if (awaitingPresentCredit && DateTime.UtcNow < waitingForPresentCreditUntilUtc)
            return "Waiting for Fête credit for the present delivery.";
        awaitingPresentCredit = false;
        carriedPresentRole = null;

        if (pendingPresentRole != null && DateTime.UtcNow < waitingForPresentPickupUntilUtc)
            return $"Picking up {pendingPresentRole}; waiting for carry state.";
        pendingPresentRole = null;

        var (role, present) = FindClosestPresentSource();
        if (present == null)
        {
            var position = objects.LocalPlayer!.Position;
            var nextSource = PresentSources.MinBy(source =>
                Vector3.DistanceSquared(position, source.Position));
            if (MoveWithin(nextSource.Position, 3f, ref pickupRouteDestination))
                return $"Moving directly to the {nextSource.Role} source.";
            return "Waiting for a present to become targetable.";
        }

        if (MoveWithin(present.Position, 3f, ref pickupRouteDestination))
            return $"Moving to {present.Name}.";
        if (DateTime.UtcNow >= nextPresentPickupUtc &&
            actions.TryInteract(present, objects.LocalPlayer!.Position))
        {
            nextPresentPickupUtc = DateTime.UtcNow.AddSeconds(2);
            waitingForPresentPickupUntilUtc = DateTime.UtcNow.AddSeconds(10);
            pendingPresentRole = role;
        }
        return $"Picking up {present.Name}.";
    }

    private (string? Role, IGameObject? Target) FindClosestPresentSource()
    {
        string? bestRole = null;
        IGameObject? bestTarget = null;
        var position = objects.LocalPlayer?.Position ?? Vector3.Zero;
        foreach (var role in PresentSourceRoles)
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

    private IGameObject? FindClosestPresentTarget(string[] roles)
    {
        IGameObject? bestTarget = null;
        var position = objects.LocalPlayer?.Position ?? Vector3.Zero;
        foreach (var role in roles)
        {
            var target = FindTarget(role);
            if (target != null && (bestTarget == null ||
                Vector3.DistanceSquared(position, target.Position) <
                Vector3.DistanceSquared(position, bestTarget.Position)))
                bestTarget = target;
        }
        return bestTarget;
    }

    private static string[] RecipientRoles(string sourceRole) => sourceRole switch
    {
        "child-present" => ChildRecipientRoles,
        "artisan-present" => ArtisanRecipientRoles,
        "merchant-present" => MerchantRecipientRoles,
        _ => [],
    };
}
