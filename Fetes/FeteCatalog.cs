using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;

namespace AutoFete;

internal readonly record struct FeteRegistration(
    FeteDefinition Definition,
    Func<GameActions, Navmesh, IObjectTable, FeteStageHandler> CreateHandler);

internal readonly record struct FeteFate(IFate Fate, FeteDefinition? Stage);

internal static class FeteCatalog
{
    public const uint FirmamentTerritoryId = 886;

    public static readonly FeteRegistration[] Registrations =
    [
        new(TwistFete.Definition,
            static (actions, navmesh, objects) => new TwistFete(actions, navmesh, objects)),
        new(SofterStuffFete.Definition,
            static (actions, navmesh, objects) => new SofterStuffFete(actions, navmesh, objects)),
        new(ShearYakFete.Definition,
            static (actions, navmesh, objects) => new ShearYakFete(actions, navmesh, objects)),
        new(ToyHunterFete.Definition,
            static (actions, navmesh, objects) => new ToyHunterFete(actions, navmesh, objects)),
        new(PresentsFete.Definition,
            static (actions, navmesh, objects) => new PresentsFete(actions, navmesh, objects)),
    ];

    public static readonly FeteDefinition[] Stages =
        Registrations.Select(registration => registration.Definition).ToArray();

    private static readonly IReadOnlyDictionary<ushort, FeteDefinition> ByFateId =
        Stages.ToDictionary(stage => stage.FateId);
    private static readonly IReadOnlyDictionary<FeteStage, FeteDefinition> ByStage =
        Stages.ToDictionary(stage => stage.Stage);

    public static FeteDefinition? Find(ushort fateId) =>
        ByFateId.TryGetValue(fateId, out var stage) ? stage : null;

    public static FeteDefinition? Find(FeteStage stage) =>
        ByStage.TryGetValue(stage, out var definition) ? definition : null;

    public static FeteDefinition? Find(string name) =>
        Stages.FirstOrDefault(stage => string.Equals(stage.Name, name,
            StringComparison.OrdinalIgnoreCase)) is { FateId: not 0 } stage ? stage : null;

    public static FeteFate[] WithDefinitions(IFateTable fates) =>
        fates.Select(fate => new FeteFate(fate, Find(fate.FateId))).ToArray();

    public static FeteStage Next(FeteStage stage)
    {
        var index = Array.FindIndex(Stages, definition => definition.Stage == stage);
        return Stages[(index + 1) % Stages.Length].Stage;
    }
}
