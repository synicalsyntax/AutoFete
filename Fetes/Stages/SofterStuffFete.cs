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

    private bool wasCarryingToy;
    private int completedToyHandins;
    private bool toyDeliveryPending;
    private DateTime waitingForToyUntilUtc;
    private DateTime waitingForPickupUntilUtc;
    private static readonly Vector3 SofterStuffWainArea = new(50.13658f, -18.50903f, 19.80073f);

    protected override void ResetStage()
    {
        wasCarryingToy = false;
        completedToyHandins = 0;
        toyDeliveryPending = false;
        waitingForToyUntilUtc = DateTime.MinValue;
        waitingForPickupUntilUtc = DateTime.MinValue;
    }

    public override string Tick(byte handInCount)
    {
        var carryingToy = actions.IsCarryingToy;
        if (wasCarryingToy && !carryingToy && toyDeliveryPending)
            completedToyHandins++;
        if (!carryingToy)
            toyDeliveryPending = false;
        wasCarryingToy = carryingToy;

        if (completedToyHandins >= GoldCount)
            return string.Empty;

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
                    return StatusText.Format("Progress_DeliveringTo", visibleWain?.Name ?? StatusText.Get("Target_Wain"));
                return MissingTarget("wain", StatusText.Get("Target_Wain"));
            }
            if (MoveWithin(wain.Position, 3f))
                return StatusText.Format("Progress_DeliveringTo", wain.Name);
            if (TryInteract("wain", wain))
                toyDeliveryPending = true;
            return StatusText.Format("Progress_DeliveringTo", wain.Name);
        }

        // A completed toy becomes targetable after its material pile is used.
        // Prefer that toy before starting work on another pile.
        var toy = FindTarget("stuffed-toy");
        if (toy != null)
        {
            waitingForToyUntilUtc = DateTime.MinValue;
            if (MoveWithin(toy.Position, 3f))
                return string.Empty;
            if (TryInteract("stuffed-toy", toy))
                waitingForPickupUntilUtc = DateTime.UtcNow.AddSeconds(8);
            return StatusText.Format("Progress_PickingUp", toy.Name);
        }

        if (DateTime.UtcNow < waitingForPickupUntilUtc)
            return string.Empty;
        if (DateTime.UtcNow < waitingForToyUntilUtc)
            return string.Empty;

        var materials = FindTarget("materials");
        if (materials == null)
            return MissingTarget("materials", StatusText.Get("Target_StuffedToyMaterials"));
        if (MoveWithin(materials.Position, 3f))
            return string.Empty;
        if (TryInteract("materials", materials))
            waitingForToyUntilUtc = DateTime.UtcNow.AddSeconds(20);
        return string.Empty;
    }
}
