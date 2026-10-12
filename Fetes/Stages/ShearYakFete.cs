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
    private int completedYakHandins;
    private int previousFleeceCount = -1;

    protected override void ResetStage()
    {
        nextClipperUtc = DateTime.MinValue;
        yakInRangeSinceUtc = DateTime.MinValue;
        completedYakHandins = 0;
        previousFleeceCount = -1;
    }

    public override string? Tick(byte handInCount)
    {
        var fleeceCount = GameActions.GetKeyItemCount(YakFleeceEventItemId);
        if (fleeceCount < 0)
            return string.Empty;

        if (previousFleeceCount >= 3 && fleeceCount == 0)
            completedYakHandins++;
        previousFleeceCount = fleeceCount;

        completedYakHandins = Math.Max(completedYakHandins, handInCount);
        if (completedYakHandins >= GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return string.Empty;
        }

        if (fleeceCount >= 3)
        {
            var chocobo = FindTarget("chirpy-chocobo");
            if (chocobo == null)
            {
                actions.ClearTarget();
                return MissingTarget("chirpy-chocobo", StatusText.Get("Target_ChirpyChocobo"));
            }
            if (MoveWithin(chocobo.Position, 3f))
            {
                actions.ClearTarget();
                return StatusText.Format("Progress_DeliveringTo", chocobo.Name);
            }
            actions.SelectTarget(chocobo);
            TryInteract("chirpy-chocobo", chocobo);
            return StatusText.Format("Progress_DeliveringTo", chocobo.Name);
        }

        var fleece = FindTarget("yak-fleece");
        if (fleece != null)
        {
            if (MoveWithin(fleece.Position, 3f))
                return string.Empty;
            TryInteract("yak-fleece", fleece);
            return StatusText.Get("Progress_CollectingYakFleece");
        }

        if (DateTime.UtcNow < nextClipperUtc)
            return string.Empty;

        var yak = FindTarget("wooly-yak");
        if (yak == null)
        {
            yakInRangeSinceUtc = DateTime.MinValue;
            return MissingTarget("wooly-yak", StatusText.Get("Target_WoolyYak"));
        }
        if (MoveWithin(yak.Position, 20f))
        {
            yakInRangeSinceUtc = DateTime.MinValue;
            return string.Empty;
        }

        var now = DateTime.UtcNow;
        if (yakInRangeSinceUtc == DateTime.MinValue)
            yakInRangeSinceUtc = now;
        if (now - yakInRangeSinceUtc < TimeSpan.FromMilliseconds(500))
            return string.Empty;

        if (actions.TryUseFeteAction(FateId, ClipperAction, yak, out var actionResult))
        {
            nextClipperUtc = DateTime.UtcNow.AddSeconds(8);
            yakInRangeSinceUtc = DateTime.MinValue;
            return actionResult;
        }
        return string.Empty;
    }
}
