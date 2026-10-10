using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace AutoFete;

public static class FeteTargets
{
    // BNpcName rows match NPC NameIds; EObjName rows match EventObj BaseIds.
    public static readonly IReadOnlyDictionary<FeteStage, string[]> Roles =
        FeteCatalog.Stages.ToDictionary(stage => stage.Stage,
            stage => stage.Targets.Keys.ToArray());

    public static IReadOnlyList<TargetHint> Hints(FeteStage stage, string role) =>
        FeteCatalog.Find(stage) is { } definition
            ? Hints(definition, role)
            : Array.Empty<TargetHint>();

    internal static IReadOnlyList<TargetHint> Hints(FeteDefinition definition, string role) =>
        definition.Targets.TryGetValue(role, out var hints)
            ? hints
            : Array.Empty<TargetHint>();

    public static bool Matches(FeteStage stage, string role, IGameObject target) =>
        FeteCatalog.Find(stage) is { } definition && Matches(definition, role, target);

    internal static bool Matches(FeteDefinition definition, string role, IGameObject target) =>
        Hints(definition, role).Any(hint => hint.Sheet switch
        {
            "BNpcName" => target is ICharacter character && character.NameId == hint.RowId,
            "EObjName" => target.ObjectKind == ObjectKind.EventObj && target.BaseId == hint.RowId,
            _ => false,
        });
}
