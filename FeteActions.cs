namespace AutoFete;

public readonly record struct FeteAction(uint Id, string Name, FeteStage Stage, string TargetRole);

public static class FeteActions
{
    // These four IDs are Action sheet rows from the supplied XIVAPI export.
    // Every action has exactly one NPC target role.
    public static readonly FeteAction[] All =
    [
        new(FeteCatalog.MightyMaximizerActionId, "Mighty Maximizer", FeteStage.Twist, "mighty-moogle"),
        new(FeteCatalog.ChirpyCheckerActionId, "Chirpy Checker", FeteStage.Twist, "chirpy-chocobo"),
        new(FeteCatalog.PerkyPeelerActionId, "Perky Peeler", FeteStage.Twist, "perky-piggy"),
        new(FeteCatalog.AetheromaticClipperActionId, "Aetheromatic Clipper", FeteStage.ShearYak, "wooly-yak"),
    ];

    public static FeteAction? ForRole(FeteStage stage, string role) =>
        All.FirstOrDefault(x => x.Stage == stage &&
                                string.Equals(x.TargetRole, role, StringComparison.OrdinalIgnoreCase)) is { Id: not 0 } action
            ? action
            : null;

}
