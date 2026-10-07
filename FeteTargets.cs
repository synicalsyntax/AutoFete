using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;

namespace AutoFete;

public sealed class TargetBinding
{
    public uint BaseId { get; set; }
    public uint NameId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
}

public readonly record struct TargetHint(string Sheet, uint RowId, string Name);

public static class FeteTargets
{
    // XIVAPI v2, game-data version 541c0c12e07da325. BNpcName IDs are
    // NameIds, not object BaseIds. EObjName IDs are object row IDs and must
    // still be checked against a live EventObj before relying on them.
    private static readonly IReadOnlyDictionary<string, TargetHint[]> Known =
        new Dictionary<string, TargetHint[]>
        {
            [Key(FeteStage.Twist, "mighty-moogle")] = [new("BNpcName", 10080, "Mighty Moogle")],
            [Key(FeteStage.Twist, "chirpy-chocobo")] = [new("BNpcName", 10081, "Chirpy Chocobo")],
            [Key(FeteStage.Twist, "perky-piggy")] = [new("BNpcName", 10082, "Perky Piggy")],

            [Key(FeteStage.SofterStuff, "materials")] = [new("EObjName", 2011601, "stuffed toy materials")],
            [Key(FeteStage.SofterStuff, "stuffed-toy")] = [new("EObjName", 2011527, "stuffed toy")],
            [Key(FeteStage.SofterStuff, "wain")] = [new("EObjName", 2011525, "wain")],

            [Key(FeteStage.ShearYak, "wooly-yak")] =
                [new("BNpcName", 3479, "wooly yak"), new("BNpcName", 11214, "wooly yak")],
            [Key(FeteStage.ShearYak, "yak-fleece")] = [new("EObjName", 2011602, "yak fleece")],
            [Key(FeteStage.ShearYak, "chirpy-chocobo")] = [new("BNpcName", 10081, "Chirpy Chocobo")],

            [Key(FeteStage.ToyHunter, "present-box")] = [new("EObjName", 2011528, "present box")],
            [Key(FeteStage.ToyHunter, "perky-piggy")] = [new("BNpcName", 10082, "Perky Piggy")],

            [Key(FeteStage.Presents, "child-present")] = [new("EObjName", 2011676, "present for children")],
            [Key(FeteStage.Presents, "merchant-present")] = [new("EObjName", 2011675, "present for merchants")],
            [Key(FeteStage.Presents, "artisan-present")] = [new("EObjName", 2011529, "present for artisans")],
            [Key(FeteStage.Presents, "firmament-boy")] = [new("BNpcName", 10092, "Firmament boy")],
            [Key(FeteStage.Presents, "firmament-girl")] = [new("BNpcName", 10093, "Firmament girl")],
            [Key(FeteStage.Presents, "foreign-merchant")] = [new("BNpcName", 10090, "foreign merchant")],
            [Key(FeteStage.Presents, "ishgardian-merchant")] = [new("BNpcName", 10089, "Ishgardian merchant")],
            [Key(FeteStage.Presents, "skysteel-engineer")] = [new("BNpcName", 10086, "Skysteel engineer")],
            [Key(FeteStage.Presents, "dzemael-stonemason")] = [new("BNpcName", 10087, "House Dzemael stonemason")],
        };

    // Roles describe what the stage handler needs; Base IDs are learned in game.
    public static readonly IReadOnlyDictionary<FeteStage, string[]> Roles =
        new Dictionary<FeteStage, string[]>
        {
            [FeteStage.Twist] = ["mighty-moogle", "chirpy-chocobo", "perky-piggy"],
            [FeteStage.SofterStuff] = ["materials", "stuffed-toy", "wain"],
            [FeteStage.ShearYak] = ["wooly-yak", "yak-fleece", "chirpy-chocobo"],
            [FeteStage.ToyHunter] = ["present-box", "perky-piggy"],
            [FeteStage.Presents] =
            [
                "child-present", "merchant-present", "artisan-present",
                "firmament-boy", "firmament-girl", "foreign-merchant",
                "ishgardian-merchant", "skysteel-engineer", "dzemael-stonemason",
            ],
        };

    public static string Key(FeteStage stage, string role) => $"{stage}.{role}";

    public static IReadOnlyList<TargetHint> Hints(FeteStage stage, string role) =>
        Known.TryGetValue(Key(stage, role), out var hints) ? hints : Array.Empty<TargetHint>();

    public static bool IsRole(FeteStage stage, string role) =>
        Roles.TryGetValue(stage, out var roles) && roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool Add(Configuration config, FeteStage stage, string role, IGameObject target)
    {
        if (!IsRole(stage, role) || target.BaseId == 0)
            return false;

        config.Targets ??= [];
        var key = Key(stage, role.ToLowerInvariant());
        if (!config.Targets.TryGetValue(key, out var bindings) || bindings == null)
        {
            bindings = [];
            config.Targets[key] = bindings;
        }

        if (bindings.Any(x => x.BaseId == target.BaseId && x.Kind == target.ObjectKind.ToString()))
            return false;

        bindings.Add(new TargetBinding
        {
            BaseId = target.BaseId,
            NameId = target is ICharacter character ? character.NameId : 0,
            Name = target.Name.ToString(),
            Kind = target.ObjectKind.ToString(),
        });
        return true;
    }

    public static IReadOnlyList<TargetBinding> Get(Configuration config, FeteStage stage, string role) =>
        config.Targets != null && config.Targets.TryGetValue(Key(stage, role), out var bindings) &&
        bindings != null
            ? bindings
            : Array.Empty<TargetBinding>();

    public static bool Matches(Configuration config, FeteStage stage, string role, IGameObject target) =>
        Get(config, stage, role).Any(x => x.BaseId == target.BaseId &&
                                        x.Kind == target.ObjectKind.ToString()) ||
        Hints(stage, role).Any(hint => hint.Sheet switch
        {
            "BNpcName" => target is ICharacter character && character.NameId == hint.RowId,
            "EObjName" => target.ObjectKind == ObjectKind.EventObj && target.BaseId == hint.RowId,
            _ => false,
        });

    public static string[] MissingRoles(Configuration config, FeteStage stage) =>
        Roles[stage].Where(role => Get(config, stage, role).Count == 0).ToArray();
}
