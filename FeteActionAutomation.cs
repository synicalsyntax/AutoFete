using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

// Runs only after the current Fête is joined. The first stage reacts to
// targetable mascots; toy making uses object and carry state; the yak stage uses inventory.
internal sealed class FeteActionAutomation(GameActions actions, Navmesh navmesh,
    IObjectTable objects)
{
    private readonly HashSet<string> handledTwistTargets = [];
    private DateTime nextClipperUtc;
    private DateTime nextFleeceInteractUtc;
    private DateTime nextHandinInteractUtc;
    private int completedYakHandins;
    private int previousFleeceCount = -1;
    private DateTime nextMaterialsInteractUtc;
    private DateTime nextToyInteractUtc;
    private DateTime nextWainInteractUtc;
    private bool wasCarryingToy;
    private int completedToyHandins;
    private bool toyDeliveryPending;
    private DateTime waitingForToyUntilUtc;
    private DateTime waitingForPickupUntilUtc;
    private DateTime nextPresentBoxInteractUtc;
    private DateTime nextPiggyInteractUtc;
    private DateTime waitingForBoxResultUntilUtc;
    private DateTime waitingForPiggyHandinUntilUtc;
    private bool presentBoxResultPending;
    private bool assumedLuckyToy;
    private int previousToyHunterHandinCount = -1;
    private static readonly string[] PresentSourceRoles =
        ["child-present", "artisan-present", "merchant-present"];
    private static readonly string[] ChildRecipientRoles = ["firmament-boy", "firmament-girl"];
    private static readonly string[] ArtisanRecipientRoles =
        ["skysteel-engineer", "dzemael-stonemason"];
    private static readonly string[] MerchantRecipientRoles =
        ["foreign-merchant", "ishgardian-merchant"];
    private static readonly Vector3 PresentPickupArea = new(50f, -19.9f, 30f);
    private static readonly Vector3 PresentDeliveryArea = new(48f, -20f, 75f);
    private static readonly Vector3 SofterStuffWainArea = new(50.13658f, -18.50903f, 19.80073f);
    private string? pendingPresentRole;
    private string? carriedPresentRole;
    private DateTime nextPresentPickupUtc;
    private DateTime nextPresentDeliveryUtc;
    private DateTime waitingForPresentPickupUntilUtc;
    private DateTime waitingForPresentCreditUntilUtc;
    private bool awaitingPresentCredit;
    private int previousPresentsHandinCount = -1;

    public void Reset()
    {
        handledTwistTargets.Clear();
        nextClipperUtc = DateTime.MinValue;
        nextFleeceInteractUtc = DateTime.MinValue;
        nextHandinInteractUtc = DateTime.MinValue;
        completedYakHandins = 0;
        previousFleeceCount = -1;
        nextMaterialsInteractUtc = DateTime.MinValue;
        nextToyInteractUtc = DateTime.MinValue;
        nextWainInteractUtc = DateTime.MinValue;
        wasCarryingToy = false;
        completedToyHandins = 0;
        toyDeliveryPending = false;
        waitingForToyUntilUtc = DateTime.MinValue;
        waitingForPickupUntilUtc = DateTime.MinValue;
        nextPresentBoxInteractUtc = DateTime.MinValue;
        nextPiggyInteractUtc = DateTime.MinValue;
        waitingForBoxResultUntilUtc = DateTime.MinValue;
        waitingForPiggyHandinUntilUtc = DateTime.MinValue;
        presentBoxResultPending = false;
        assumedLuckyToy = false;
        previousToyHunterHandinCount = -1;
        pendingPresentRole = null;
        carriedPresentRole = null;
        nextPresentPickupUtc = DateTime.MinValue;
        nextPresentDeliveryUtc = DateTime.MinValue;
        waitingForPresentPickupUntilUtc = DateTime.MinValue;
        waitingForPresentCreditUntilUtc = DateTime.MinValue;
        awaitingPresentCredit = false;
        previousPresentsHandinCount = -1;
    }

    public string Tick(FeteStage stage, ushort fateId, byte handInCount) => stage switch
    {
        FeteStage.Twist => TickTwist(fateId, handInCount),
        FeteStage.SofterStuff => TickSofterStuff(),
        FeteStage.ShearYak => TickShearYak(fateId, handInCount),
        FeteStage.ToyHunter => TickToyHunter(fateId, handInCount),
        FeteStage.Presents => TickPresents(fateId, handInCount),
        _ => $"Joined {FeteCatalog.Find(fateId)?.Name}. Stage handler still pending.",
    };

    private string TickPresents(ushort fateId, byte handInCount)
    {
        if (previousPresentsHandinCount >= 0 && handInCount > previousPresentsHandinCount)
        {
            pendingPresentRole = null;
            carriedPresentRole = null;
            awaitingPresentCredit = false;
            waitingForPresentCreditUntilUtc = DateTime.MinValue;
        }
        previousPresentsHandinCount = handInCount;

        if (handInCount >= FeteCatalog.Find(fateId)!.Value.GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Six presents delivered; waiting for the Fête to finish.";
        }

        var carryingPresent = actions.CarryingObject ||
                              objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 404) == true;
        if (carryingPresent)
        {
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
            if (MoveWithin(PresentPickupArea, 8f))
                return "Returning to the present pickup area.";
            return "Waiting for a present to become targetable.";
        }

        if (MoveWithin(present.Position, 3f))
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
            var target = actions.FindTarget(FeteStage.Presents, role);
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
            var target = actions.FindTarget(FeteStage.Presents, role);
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

    private string TickToyHunter(ushort fateId, byte handInCount)
    {
        var toyCount = GameActions.GetKeyItemCount(FeteCatalog.LuckyToyEventItemId);
        if (toyCount < 0)
            return "Waiting for the key-item inventory to load.";

        if (previousToyHunterHandinCount >= 0 && handInCount > previousToyHunterHandinCount)
        {
            assumedLuckyToy = false;
            presentBoxResultPending = false;
            waitingForPiggyHandinUntilUtc = DateTime.MinValue;
        }
        previousToyHunterHandinCount = handInCount;

        if (handInCount >= FeteCatalog.Find(fateId)!.Value.GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Four lucky toys turned in; waiting for the Fête to finish.";
        }

        // A losing box transforms the player for about five seconds. Interaction
        // attempts during that status cannot collect another present.
        if (objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 1292) == true)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            presentBoxResultPending = false;
            assumedLuckyToy = false;
            return "Transformed into a piggy; waiting to open another box.";
        }

        if (toyCount > 0)
        {
            presentBoxResultPending = false;
            assumedLuckyToy = true;
        }

        if (presentBoxResultPending && toyCount == 0)
        {
            var anotherBoxIsTargetable = objects.Any(x => x.IsTargetable &&
                FeteTargets.Matches(FeteStage.ToyHunter, "present-box", x));
            if (!anotherBoxIsTargetable)
            {
                presentBoxResultPending = false;
                assumedLuckyToy = true;
            }
            else if (DateTime.UtcNow < waitingForBoxResultUntilUtc)
                return "Opening a present box; waiting to see what was inside.";
            else
                presentBoxResultPending = false;
        }

        if (assumedLuckyToy)
        {
            var piggy = actions.FindTarget(FeteStage.ToyHunter, "perky-piggy");
            if (piggy == null)
                return "Carrying a lucky toy; looking for Perky Piggy.";
            actions.SelectTarget(piggy);
            if (MoveWithin(piggy.Position, 3f))
                return "Carrying a lucky toy to Perky Piggy.";
            if (toyCount == 0 && DateTime.UtcNow < waitingForPiggyHandinUntilUtc)
                return "Waiting for Perky Piggy to accept the lucky toy.";
            if (toyCount == 0 && waitingForPiggyHandinUntilUtc != DateTime.MinValue &&
                DateTime.UtcNow >= waitingForPiggyHandinUntilUtc)
                assumedLuckyToy = false;
            else
            {
                if (DateTime.UtcNow >= nextPiggyInteractUtc &&
                    actions.TryInteract(piggy, objects.LocalPlayer!.Position))
                {
                    nextPiggyInteractUtc = DateTime.UtcNow.AddSeconds(2);
                    waitingForPiggyHandinUntilUtc = DateTime.UtcNow.AddSeconds(6);
                }
                return $"Turning in a lucky toy to Perky Piggy ({handInCount}/4).";
            }
        }

        var box = actions.FindTarget(FeteStage.ToyHunter, "present-box");
        if (box == null)
            return "Looking for a targetable present box.";
        if (MoveWithin(box.Position, 3f))
            return "Moving to a present box.";
        if (DateTime.UtcNow >= nextPresentBoxInteractUtc &&
            actions.TryInteract(box, objects.LocalPlayer!.Position))
        {
            nextPresentBoxInteractUtc = DateTime.UtcNow.AddSeconds(2);
            waitingForBoxResultUntilUtc = DateTime.UtcNow.AddSeconds(8);
            presentBoxResultPending = true;
        }
        return "Opening a present box.";
    }

    private string TickSofterStuff()
    {
        var carryingToy = actions.IsCarryingToy;
        if (wasCarryingToy && !carryingToy && toyDeliveryPending)
            completedToyHandins++;
        if (!carryingToy)
            toyDeliveryPending = false;
        wasCarryingToy = carryingToy;

        if (completedToyHandins >= 3)
            return "Three toy deliveries observed; waiting for the Fête to finish.";

        if (carryingToy)
        {
            var wain = actions.FindTarget(FeteStage.SofterStuff, "wain");
            if (wain == null)
            {
                var visibleWain = objects
                    .Where(x => FeteTargets.Matches(FeteStage.SofterStuff, "wain", x))
                    .OrderBy(x => Vector3.DistanceSquared(objects.LocalPlayer!.Position, x.Position))
                    .FirstOrDefault();
                if (MoveWithin(visibleWain?.Position ?? SofterStuffWainArea, 8f))
                    return "Carrying a stuffed toy toward the wain.";
                return "Carrying a stuffed toy; waiting for the wain to become targetable.";
            }
            if (MoveWithin(wain.Position, 3f))
                return "Carrying a stuffed toy to a wain.";
            if (DateTime.UtcNow >= nextWainInteractUtc &&
                actions.TryInteract(wain, objects.LocalPlayer!.Position))
            {
                nextWainInteractUtc = DateTime.UtcNow.AddSeconds(2);
                toyDeliveryPending = true;
            }
            return $"Delivering a stuffed toy to a wain ({completedToyHandins}/3 observed).";
        }

        // A completed toy becomes targetable after its material pile is used.
        // Prefer that toy before starting work on another pile.
        var toy = actions.FindTarget(FeteStage.SofterStuff, "stuffed-toy");
        if (toy != null)
        {
            waitingForToyUntilUtc = DateTime.MinValue;
            if (MoveWithin(toy.Position, 3f))
                return "Moving to a finished stuffed toy.";
            if (DateTime.UtcNow >= nextToyInteractUtc &&
                actions.TryInteract(toy, objects.LocalPlayer!.Position))
            {
                nextToyInteractUtc = DateTime.UtcNow.AddSeconds(2);
                waitingForPickupUntilUtc = DateTime.UtcNow.AddSeconds(8);
            }
            return "Picking up the finished stuffed toy.";
        }

        if (DateTime.UtcNow < waitingForPickupUntilUtc)
            return "Waiting for the stuffed toy pickup to finish.";
        if (DateTime.UtcNow < waitingForToyUntilUtc)
            return "Making a stuffed toy; waiting for the finished toy.";

        var materials = actions.FindTarget(FeteStage.SofterStuff, "materials");
        if (materials == null)
            return "Looking for targetable stuffed toy materials.";
        if (MoveWithin(materials.Position, 3f))
            return "Moving to stuffed toy materials.";
        if (DateTime.UtcNow >= nextMaterialsInteractUtc &&
            actions.TryInteract(materials, objects.LocalPlayer!.Position))
        {
            nextMaterialsInteractUtc = DateTime.UtcNow.AddSeconds(2);
            waitingForToyUntilUtc = DateTime.UtcNow.AddSeconds(20);
        }
        return "Making a stuffed toy; waiting for the finished toy.";
    }

    private string TickTwist(ushort fateId, byte handInCount)
    {
        if (handInCount >= FeteCatalog.Find(fateId)!.Value.GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Six mascot actions complete; waiting for the Fête to finish.";
        }

        // During each turn, the active mascot becomes targetable. Its EmoteId
        // stays zero, so targetability is the reliable signal for the action.
        FeteAction? activeAction = null;
        IGameObject? target = null;
        foreach (var action in FeteActions.All.Where(x => x.Stage == FeteStage.Twist))
        {
            var npc = actions.FindTarget(FeteStage.Twist, action.TargetRole);
            if (npc == null)
                handledTwistTargets.Remove(action.TargetRole);
            else if (!handledTwistTargets.Contains(action.TargetRole) && target == null)
            {
                activeAction = action;
                target = npc;
            }
        }
        if (activeAction == null || target == null)
            return "Watching for the next targetable mascot.";

        var player = objects.LocalPlayer;
        if (player != null && Vector3.Distance(player.Position, target.Position) > 20f)
        {
            if (!navmesh.IsMoving()) navmesh.MoveNear(target.Position, 15f);
            return $"Moving into action range of {target.Name}.";
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        if (actions.TryUseFeteAction(fateId, activeAction.Value, out var result))
            handledTwistTargets.Add(activeAction.Value.TargetRole);
        return result;
    }

    private string TickShearYak(ushort fateId, byte handInCount)
    {
        var fleeceCount = GameActions.GetKeyItemCount(FeteCatalog.YakFleeceEventItemId);
        if (fleeceCount < 0)
            return "Waiting for the key-item inventory to load.";

        if (previousFleeceCount >= 3 && fleeceCount == 0)
            completedYakHandins++;
        previousFleeceCount = fleeceCount;

        completedYakHandins = Math.Max(completedYakHandins, handInCount);
        if (completedYakHandins >= FeteCatalog.Find(fateId)!.Value.GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Four fleece hand-ins complete; waiting for the Fête to finish.";
        }

        if (fleeceCount >= 3)
        {
            var chocobo = actions.FindTarget(FeteStage.ShearYak, "chirpy-chocobo");
            if (chocobo == null)
                return "Carrying three fleece; waiting for Chirpy Chocobo.";
            actions.SelectTarget(chocobo);
            if (MoveWithin(chocobo.Position, 3f))
                return "Moving to Chirpy Chocobo with fleece.";
            if (DateTime.UtcNow >= nextHandinInteractUtc &&
                actions.TryInteract(chocobo, objects.LocalPlayer!.Position))
                nextHandinInteractUtc = DateTime.UtcNow.AddSeconds(2);
            return $"Turning in fleece to Chirpy Chocobo ({completedYakHandins} observed hand-ins).";
        }

        var fleece = actions.FindTarget(FeteStage.ShearYak, "yak-fleece");
        if (fleece != null)
        {
            if (MoveWithin(fleece.Position, 3f))
                return $"Moving to yak fleece ({fleeceCount}/3 held).";
            if (DateTime.UtcNow >= nextFleeceInteractUtc &&
                actions.TryInteract(fleece, objects.LocalPlayer!.Position))
                nextFleeceInteractUtc = DateTime.UtcNow.AddSeconds(2);
            return $"Collecting yak fleece ({fleeceCount}/3 held).";
        }

        if (DateTime.UtcNow < nextClipperUtc)
            return $"Waiting for fleece to appear ({fleeceCount}/3 held).";

        var yak = actions.FindTarget(FeteStage.ShearYak, "wooly-yak");
        if (yak == null)
            return "Waiting for the Wooly Yak.";
        if (MoveWithin(yak.Position, 20f))
            return "Moving into clipper range of the Wooly Yak.";

        var clipper = FeteActions.ForRole(FeteStage.ShearYak, "wooly-yak");
        if (clipper != null && actions.TryUseFeteAction(fateId, clipper.Value, out var actionResult))
        {
            nextClipperUtc = DateTime.UtcNow.AddSeconds(8);
            return actionResult;
        }
        return "Waiting to use the Aetheromatic Clipper on the Wooly Yak.";
    }

    private bool MoveWithin(Vector3 position, float range)
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
}
