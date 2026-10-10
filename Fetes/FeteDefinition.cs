using System.Numerics;

namespace AutoFete;

internal readonly record struct FeteDefinition(
    FeteStage Stage,
    ushort FateId,
    string Name,
    int GoldCount,
    Vector3 Waypoint,
    IReadOnlyDictionary<string, TargetHint[]> Targets,
    bool WaitForJoinConfirmation,
    bool UsesHandinDialogs);
