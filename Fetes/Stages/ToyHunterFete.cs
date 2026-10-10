using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class ToyHunterFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1715;
    public const string Name = "Toy Hunter";
    public const int GoldCount = 4;
    public const uint LuckyToyEventItemId = 2003082;
    public static readonly Vector3 Waypoint = new(-76.354965f, 0f, -0.04517188f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["present-box"] = [new("EObjName", 2011528, "present box")],
            ["perky-piggy"] = [new("BNpcName", 10082, "Perky Piggy")],
        };
    public static readonly FeteDefinition Definition = new(
        FeteStage.ToyHunter, FateId, Name, GoldCount, Waypoint, Targets,
        WaitForJoinConfirmation: true, UsesHandinDialogs: true);

    private DateTime nextPresentBoxInteractUtc;
    private DateTime nextPiggyInteractUtc;
    private DateTime waitingForBoxResultUntilUtc;
    private DateTime waitingForPiggyHandinUntilUtc;
    private bool presentBoxResultPending;
    private bool assumedLuckyToy;
    private int previousToyHunterHandinCount = -1;
    private ulong? lastOpenedBoxId;
    private Vector3? boxRouteDestination;

    public override void Reset()
    {
        nextPresentBoxInteractUtc = DateTime.MinValue;
        nextPiggyInteractUtc = DateTime.MinValue;
        waitingForBoxResultUntilUtc = DateTime.MinValue;
        waitingForPiggyHandinUntilUtc = DateTime.MinValue;
        presentBoxResultPending = false;
        assumedLuckyToy = false;
        previousToyHunterHandinCount = -1;
        lastOpenedBoxId = null;
        boxRouteDestination = null;
    }

    public override string Tick(byte handInCount) => TickToyHunter(handInCount);

    private string TickToyHunter(byte handInCount)
    {
        var toyCount = GameActions.GetKeyItemCount(LuckyToyEventItemId);
        if (toyCount < 0)
            return "Waiting for the key-item inventory to load.";

        if (previousToyHunterHandinCount >= 0 && handInCount > previousToyHunterHandinCount)
        {
            assumedLuckyToy = false;
            presentBoxResultPending = false;
            waitingForPiggyHandinUntilUtc = DateTime.MinValue;
        }
        previousToyHunterHandinCount = handInCount;

        if (handInCount >= GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Four lucky toys turned in; waiting for the Fête to finish.";
        }

        // A losing box transforms the player for about five seconds. Walk to
        // another box now, then wait for the status to end before interacting.
        if (objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 1292) == true)
        {
            presentBoxResultPending = false;
            assumedLuckyToy = false;
            actions.ClearTarget();
            var nextBox = FindNextPresentBox(onlyTargetable: false);
            if (nextBox == null)
                return "Transformed into a piggy; looking for another present box.";
            if (MoveWithin(nextBox.Position, 3f, ref boxRouteDestination))
                return "Transformed into a piggy; moving to the next present box.";
            return "Transformed into a piggy; waiting to open the next present box.";
        }

        if (toyCount > 0)
        {
            presentBoxResultPending = false;
            assumedLuckyToy = true;
        }

        if (presentBoxResultPending && toyCount == 0)
        {
            var anotherBoxIsTargetable = objects.Any(x => x.IsTargetable &&
                Matches("present-box", x));
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
            var piggy = FindTarget("perky-piggy");
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

        var box = FindNextPresentBox(onlyTargetable: true);
        if (box == null)
            return "Looking for a targetable present box.";
        if (MoveWithin(box.Position, 3f, ref boxRouteDestination))
            return "Moving to a present box.";
        if (DateTime.UtcNow >= nextPresentBoxInteractUtc &&
            actions.TryInteract(box, objects.LocalPlayer!.Position))
        {
            lastOpenedBoxId = box.GameObjectId;
            nextPresentBoxInteractUtc = DateTime.UtcNow.AddSeconds(2);
            waitingForBoxResultUntilUtc = DateTime.UtcNow.AddSeconds(8);
            presentBoxResultPending = true;
        }
        return "Opening a present box.";
    }

    private IGameObject? FindNextPresentBox(bool onlyTargetable)
    {
        var playerPosition = objects.LocalPlayer?.Position ?? Vector3.Zero;
        return objects
            .Where(x => Matches("present-box", x) && (!onlyTargetable || x.IsTargetable))
            .OrderBy(x => x.GameObjectId == lastOpenedBoxId)
            .ThenBy(x => !x.IsTargetable)
            .ThenBy(x => Vector3.DistanceSquared(playerPosition, x.Position))
            .FirstOrDefault();
    }
}
