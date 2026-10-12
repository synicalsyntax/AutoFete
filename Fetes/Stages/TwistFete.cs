using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class TwistFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1712;
    public const string Name = "A Twist of Fête";
    public const int MinPointCount = 6;
    public const uint MightyMaximizerActionId = 24277;
    public const uint ChirpyCheckerActionId = 24278;
    public const uint PerkyPeelerActionId = 24279;
    public static readonly Vector3 Waypoint = new(10.060686f, 0.29999006f, -145.31203f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["mighty-moogle"] = [TargetHint.MightyMoogle],
            ["chirpy-chocobo"] = [TargetHint.ChirpyChocobo],
            ["perky-piggy"] = [TargetHint.PerkyPiggy],
        };
    public static readonly FeteAction[] Actions =
    [
        new(MightyMaximizerActionId, "Mighty Maximizer", FeteStage.Twist, "mighty-moogle"),
        new(ChirpyCheckerActionId, "Chirpy Checker", FeteStage.Twist, "chirpy-chocobo"),
        new(PerkyPeelerActionId, "Perky Peeler", FeteStage.Twist, "perky-piggy"),
    ];
    public static readonly FeteDefinition Definition = new(
        FeteStage.Twist, FateId, Name, MinPointCount, Waypoint, Targets,
        WaitForJoinConfirmation: true, UsesHandinDialogs: false);

    private readonly HashSet<string> handledTwistTargets = [];

    protected override void ResetStage()
    {
        handledTwistTargets.Clear();
    }

    public override string Tick(byte handInCount, bool continueAfterFullCredit)
    {
        if (ShouldComplete(handInCount, continueAfterFullCredit))
            return CompleteStage(handInCount);

        // During each turn, the active mascot becomes targetable. Its EmoteId
        // stays zero, so targetability is the reliable signal for the action.
        FeteAction? activeAction = null;
        IGameObject? target = null;
        foreach (var action in Actions)
        {
            var npc = FindTarget(action.TargetRole);
            if (npc == null)
                handledTwistTargets.Remove(action.TargetRole);
            else if (!handledTwistTargets.Contains(action.TargetRole) && target == null)
            {
                activeAction = action;
                target = npc;
            }
        }
        if (activeAction == null || target == null)
            return string.Empty;

        var player = objects.LocalPlayer;
        if (player != null && Vector3.Distance(player.Position, target.Position) > 20f)
        {
            if (!navmesh.IsMoving()) navmesh.MoveNear(target.Position, 15f);
            return StatusText.Format("Progress_MovingTo", target.Name);
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        if (actions.TryUseFeteAction(FateId, activeAction.Value, target, out var result))
            handledTwistTargets.Add(activeAction.Value.TargetRole);
        return result.Length > 0
            ? result
            : StatusText.Format("Progress_PerformingEmoteFor", target.Name);
    }
}
