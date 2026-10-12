using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class ShearYakFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1714;
    public const string Name = "Shear-a-Yak";
    public const int GoldCount = 4;
    private const int FleecePerHandin = 3;
    public const uint AetheromaticClipperActionId = 24280;
    public const uint YakFleeceEventItemId = 2003081;
    public static readonly Vector3 Waypoint = new(10.060686f, 0.29999006f, -145.31203f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["wooly-yak"] = [new("BNpcName", 3479, "wooly yak"), new("BNpcName", 11214, "wooly yak")],
            ["yak-fleece"] = [new("EObjName", 2011602, "yak fleece")],
            ["chirpy-chocobo"] = [new("BNpcName", 10081, "Chirpy Chocobo")],
        };
    private static readonly FeteAction ClipperAction =
        new(AetheromaticClipperActionId, "Aetheromatic Clipper", FeteStage.ShearYak, "wooly-yak");
    public static readonly FeteDefinition Definition = new(
        FeteStage.ShearYak, FateId, Name, GoldCount, Waypoint, Targets,
        WaitForJoinConfirmation: true, UsesHandinDialogs: true);

    private DateTime nextClipperUtc;
    private DateTime yakInRangeSinceUtc;
    private DateTime showFullFleeceUntilUtc;
    private int completedYakHandins;
    private int previousFleeceCount = -1;
    private string? fleeceName;

    protected override void ResetStage()
    {
        nextClipperUtc = DateTime.MinValue;
        yakInRangeSinceUtc = DateTime.MinValue;
        showFullFleeceUntilUtc = DateTime.MinValue;
        completedYakHandins = 0;
        previousFleeceCount = -1;
        fleeceName = null;
    }

    public override string? Tick(byte handInCount)
    {
        var fleeceCount = GameActions.GetKeyItemCount(YakFleeceEventItemId);
        if (fleeceCount < 0)
            return string.Empty;

        if (fleeceCount >= FleecePerHandin && previousFleeceCount < FleecePerHandin)
            showFullFleeceUntilUtc = DateTime.UtcNow.AddSeconds(1);
        if (previousFleeceCount >= FleecePerHandin && fleeceCount == 0)
            completedYakHandins++;
        previousFleeceCount = fleeceCount;

        completedYakHandins = Math.Max(completedYakHandins, handInCount);
        if (completedYakHandins >= GoldCount)
            return CompleteStage(completedYakHandins);

        if (fleeceCount >= FleecePerHandin)
        {
            var fullFleeceStatus = StatusText.Format("Progress_CollectingWithCount",
                FleeceName, fleeceCount, FleecePerHandin);
            var chocobo = FindTarget("chirpy-chocobo");
            if (chocobo == null)
            {
                actions.ClearTarget();
                var missingTarget = MissingTarget("chirpy-chocobo", TargetName("chirpy-chocobo"));
                return missingTarget.Length > 0 ? missingTarget : fullFleeceStatus;
            }
            var deliveryStatus = StatusText.Format("Progress_DeliveringItemTo",
                FleeceName, chocobo.Name);
            var status = DateTime.UtcNow < showFullFleeceUntilUtc
                ? fullFleeceStatus
                : deliveryStatus;
            if (MoveWithin(chocobo.Position, 3f))
            {
                actions.ClearTarget();
                return status;
            }
            actions.SelectTarget(chocobo);
            TryInteract("chirpy-chocobo", chocobo);
            return status;
        }

        var fleece = FindTarget("yak-fleece");
        if (fleece != null)
            fleeceName = fleece.Name.ToString();
        var collectingStatus = StatusText.Format("Progress_CollectingWithCount",
            FleeceName, fleeceCount, FleecePerHandin);
        if (fleece != null)
        {
            if (MoveWithin(fleece.Position, 3f))
                return collectingStatus;
            TryInteract("yak-fleece", fleece);
            return collectingStatus;
        }

        if (DateTime.UtcNow < nextClipperUtc)
            return collectingStatus;

        var yak = FindTarget("wooly-yak");
        if (yak == null)
        {
            yakInRangeSinceUtc = DateTime.MinValue;
            var missingTarget = MissingTarget("wooly-yak", TargetName("wooly-yak"));
            return missingTarget.Length > 0 ? missingTarget : collectingStatus;
        }
        var shearingStatus = StatusText.Format("Progress_ShearingYak", yak.Name);
        if (MoveWithin(yak.Position, 20f))
        {
            yakInRangeSinceUtc = DateTime.MinValue;
            return shearingStatus;
        }

        var now = DateTime.UtcNow;
        if (yakInRangeSinceUtc == DateTime.MinValue)
            yakInRangeSinceUtc = now;
        if (now - yakInRangeSinceUtc < TimeSpan.FromMilliseconds(500))
            return shearingStatus;

        if (actions.TryUseFeteAction(FateId, ClipperAction, yak, out var actionResult))
        {
            nextClipperUtc = DateTime.UtcNow.AddSeconds(8);
            yakInRangeSinceUtc = DateTime.MinValue;
            return actionResult.Length > 0 ? actionResult : shearingStatus;
        }
        return shearingStatus;
    }

    private string FleeceName => fleeceName ?? TargetName("yak-fleece");
}
