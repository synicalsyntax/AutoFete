using Dalamud.Plugin.Services;

namespace AutoFete;

// Holds stage state; the registry supplies each module's handler factory.
internal sealed class FeteActionAutomation(GameActions actions, Navmesh navmesh, IObjectTable objects)
{
    private readonly IReadOnlyDictionary<FeteStage, FeteStageHandler> handlers =
        FeteCatalog.Registrations.ToDictionary(registration => registration.Definition.Stage,
            registration => registration.CreateHandler(actions, navmesh, objects));

    public void Reset()
    {
        foreach (var handler in handlers.Values)
            handler.Reset();
    }

    public string Tick(ushort fateId, byte handInCount) =>
        FeteCatalog.Find(fateId) is { } definition &&
        handlers.TryGetValue(definition.Stage, out var handler)
            ? handler.Tick(handInCount)
            : $"Joined {FeteCatalog.Find(fateId)?.Name}. Stage handler still pending.";
}
