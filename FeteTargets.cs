using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;

namespace AutoFete;

public readonly record struct TargetHint(string Sheet, uint RowId, string Name);

public static class FeteTargets
{
    // Static game-data bindings. BNpcName rows match NPC NameIds; EObjName
    // rows match EventObj BaseIds. Stage handlers never read target settings.
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

    // Roles describe what each stage handler needs.
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

    public static bool Matches(FeteStage stage, string role, IGameObject target) =>
        Hints(stage, role).Any(hint => hint.Sheet switch
        {
            "BNpcName" => target is ICharacter character && character.NameId == hint.RowId,
            "EObjName" => target.ObjectKind == ObjectKind.EventObj && target.BaseId == hint.RowId,
            _ => false,
        });
}
