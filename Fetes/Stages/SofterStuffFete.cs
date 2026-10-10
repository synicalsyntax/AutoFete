using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class SofterStuffFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1713;
    public const string Name = "Made of Softer Stuff";
    public const int GoldCount = 3;
    public static readonly Vector3 Waypoint = new(36.96617f, -19.90003f, 87.32608f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["materials"] = [new("EObjName", 2011601, "stuffed toy materials")],
            ["stuffed-toy"] = [new("EObjName", 2011527, "stuffed toy")],
            ["wain"] = [new("EObjName", 2011525, "wain")],
        };
    public static readonly FeteDefinition Definition = new(
        FeteStage.SofterStuff, FateId, Name, GoldCount, Waypoint, Targets,
        WaitForJoinConfirmation: false, UsesHandinDialogs: true);

    private DateTime nextMaterialsInteractUtc;
    private DateTime nextToyInteractUtc;
    private DateTime nextWainInteractUtc;
    private bool wasCarryingToy;
    private int completedToyHandins;
    private bool toyDeliveryPending;
    private DateTime waitingForToyUntilUtc;
    private DateTime waitingForPickupUntilUtc;
    private static readonly Vector3 SofterStuffWainArea = new(50.13658f, -18.50903f, 19.80073f);

    public override void Reset()
    {
        nextMaterialsInteractUtc = DateTime.MinValue;
        nextToyInteractUtc = DateTime.MinValue;
        nextWainInteractUtc = DateTime.MinValue;
        wasCarryingToy = false;
        completedToyHandins = 0;
        toyDeliveryPending = false;
        waitingForToyUntilUtc = DateTime.MinValue;
        waitingForPickupUntilUtc = DateTime.MinValue;
    }

    public override string Tick(byte handInCount) => TickSofterStuff();

    private string TickSofterStuff()
    {
        var carryingToy = actions.IsCarryingToy;
        if (wasCarryingToy && !carryingToy && toyDeliveryPending)
            completedToyHandins++;
        if (!carryingToy)
            toyDeliveryPending = false;
        wasCarryingToy = carryingToy;

        if (completedToyHandins >= GoldCount)
            return "Three toy deliveries observed; waiting for the Fête to finish.";

        if (carryingToy)
        {
            var wain = FindTarget("wain");
            if (wain == null)
            {
                var visibleWain = objects
                    .Where(x => Matches("wain", x))
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
        var toy = FindTarget("stuffed-toy");
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

        var materials = FindTarget("materials");
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
}
