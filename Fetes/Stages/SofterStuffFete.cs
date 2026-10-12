using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class SofterStuffFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1713;
    public const string Name = "Made of Softer Stuff";
    public const int MinPointCount = 3;
    public static readonly Vector3 Waypoint = new(36.96617f, -19.90003f, 87.32608f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["materials"] = [TargetHint.Interactable(2011601, "stuffed toy materials")],
            ["stuffed-toy"] = [TargetHint.Interactable(2011527, "stuffed toy")],
            ["wain"] = [TargetHint.Interactable(2011525, "wain")],
        };
    public static readonly FeteDefinition Definition = new(
        FeteStage.SofterStuff, FateId, Name, MinPointCount, Waypoint, Targets,
        WaitForJoinConfirmation: false, UsesHandinDialogs: true);

    private int previousHandInCount = -1;
    private string? toyName;
    private DateTime waitingForToyUntilUtc;
    private DateTime waitingForPickupUntilUtc;
    private static readonly Vector3 SofterStuffWainArea = new(50.13658f, -18.50903f, 19.80073f);

    protected override void ResetStage()
    {
        previousHandInCount = -1;
        toyName = null;
        waitingForToyUntilUtc = DateTime.MinValue;
        waitingForPickupUntilUtc = DateTime.MinValue;
    }

    public override string Tick(byte handInCount, bool continueAfterFullCredit)
    {
        var carryingToy = actions.IsCarryingToy;
        if (previousHandInCount >= 0 && handInCount > previousHandInCount)
            toyName = null;
        previousHandInCount = handInCount;

        if (ShouldComplete(handInCount, continueAfterFullCredit))
            return CompleteStage(handInCount);

        if (carryingToy)
        {
            var wain = FindTarget("wain");
            if (wain == null)
            {
                var visibleWain = objects
                    .Where(x => Matches("wain", x))
                    .OrderBy(x => Vector3.DistanceSquared(objects.LocalPlayer!.Position, x.Position))
                    .FirstOrDefault();
                var recipient = visibleWain?.Name.ToString() ?? TargetName("wain");
                var deliveryStatus = StatusText.Format("Progress_DeliveringItemTo",
                    toyName ?? TargetName("stuffed-toy"), recipient);
                if (MoveWithin(visibleWain?.Position ?? SofterStuffWainArea, 8f))
                    return deliveryStatus;
                var missingTarget = MissingTarget("wain", TargetName("wain"));
                return missingTarget.Length > 0 ? missingTarget : deliveryStatus;
            }
            var wainDeliveryStatus = StatusText.Format("Progress_DeliveringItemTo",
                toyName ?? TargetName("stuffed-toy"), wain.Name);
            if (MoveWithin(wain.Position, 3f))
                return wainDeliveryStatus;
            TryInteract("wain", wain);
            return wainDeliveryStatus;
        }

        // A completed toy becomes targetable after its material pile is used.
        // Prefer that toy before starting work on another pile.
        var toy = FindTarget("stuffed-toy");
        if (toy != null)
        {
            waitingForToyUntilUtc = DateTime.MinValue;
            toyName = toy.Name.ToString();
            if (MoveWithin(toy.Position, 3f))
                return StatusText.Format("Progress_Collecting", toy.Name);
            if (TryInteract("stuffed-toy", toy))
                waitingForPickupUntilUtc = DateTime.UtcNow.AddSeconds(8);
            return StatusText.Format("Progress_Collecting", toy.Name);
        }

        if (DateTime.UtcNow < waitingForPickupUntilUtc)
            return StatusText.Format("Progress_Collecting", toyName ?? TargetName("stuffed-toy"));
        if (DateTime.UtcNow < waitingForToyUntilUtc)
            return StatusText.Get("Progress_MakingStuffedToy");

        var materials = FindTarget("materials");
        if (materials == null)
        {
            var missingTarget = MissingTarget("materials", TargetName("materials"));
            return missingTarget.Length > 0
                ? missingTarget
                : StatusText.Format("Progress_Collecting", TargetName("materials"));
        }
        if (MoveWithin(materials.Position, 3f))
            return StatusText.Format("Progress_Collecting", materials.Name);
        if (TryInteract("materials", materials))
            waitingForToyUntilUtc = DateTime.UtcNow.AddSeconds(20);
        return StatusText.Format("Progress_Collecting", materials.Name);
    }
}
