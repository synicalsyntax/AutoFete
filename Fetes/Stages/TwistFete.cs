using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

internal sealed class TwistFete(GameActions actions, Navmesh navmesh, IObjectTable objects)
    : FeteStageHandler(Definition, actions, navmesh, objects)
{
    public const ushort FateId = 1712;
    public const string Name = "A Twist of Fête";
    public const int GoldCount = 6;
    public const uint MightyMaximizerActionId = 24277;
    public const uint ChirpyCheckerActionId = 24278;
    public const uint PerkyPeelerActionId = 24279;
    public static readonly Vector3 Waypoint = new(10.060686f, 0.29999006f, -145.31203f);
    public static readonly IReadOnlyDictionary<string, TargetHint[]> Targets =
        new Dictionary<string, TargetHint[]>
        {
            ["mighty-moogle"] = [new("BNpcName", 10080, "Mighty Moogle")],
            ["chirpy-chocobo"] = [new("BNpcName", 10081, "Chirpy Chocobo")],
            ["perky-piggy"] = [new("BNpcName", 10082, "Perky Piggy")],
        };
    public static readonly FeteAction[] Actions =
    [
        new(MightyMaximizerActionId, "Mighty Maximizer", FeteStage.Twist, "mighty-moogle"),
        new(ChirpyCheckerActionId, "Chirpy Checker", FeteStage.Twist, "chirpy-chocobo"),
        new(PerkyPeelerActionId, "Perky Peeler", FeteStage.Twist, "perky-piggy"),
    ];
    public static readonly FeteDefinition Definition = new(
        FeteStage.Twist, FateId, Name, GoldCount, Waypoint, Targets,
        WaitForJoinConfirmation: true, UsesHandinDialogs: false);

    private readonly HashSet<string> handledTwistTargets = [];

    public override void Reset()
    {
        handledTwistTargets.Clear();
    }

    public override string Tick(byte handInCount) => TickTwist(handInCount);

    private string TickTwist(byte handInCount)
    {
        if (handInCount >= GoldCount)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return "Six mascot actions complete; waiting for the Fête to finish.";
        }

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
            return "Watching for the next targetable mascot.";

        var player = objects.LocalPlayer;
        if (player != null && Vector3.Distance(player.Position, target.Position) > 20f)
        {
            if (!navmesh.IsMoving()) navmesh.MoveNear(target.Position, 15f);
            return $"Moving into action range of {target.Name}.";
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        if (actions.TryUseFeteAction(FateId, activeAction.Value, target, out var result))
            handledTwistTargets.Add(activeAction.Value.TargetRole);
        return result;
    }
}
