using System.Numerics;
using System.Text.Json;

namespace AutoFete;

public sealed class Configuration
{
    public RunMode Mode { get; set; } = RunMode.OneSequence;
    public bool DebugEnabled { get; set; }

    public static Configuration Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path)) ?? new Configuration()
        : new Configuration();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Mode, DebugEnabled },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

public enum RunMode
{
    OneSequence,
    RemainingCycle,
}

public enum FeteStage
{
    Twist,
    SofterStuff,
    ShearYak,
    ToyHunter,
    Presents,
}

public static class FeteCatalog
{
    public const uint FirmamentTerritoryId = 886;
    public const uint MightyMaximizerActionId = 24277;
    public const uint ChirpyCheckerActionId = 24278;
    public const uint PerkyPeelerActionId = 24279;
    public const uint AetheromaticClipperActionId = 24280;
    public const uint YakFleeceEventItemId = 2003081;
    public const uint LuckyToyEventItemId = 2003082;

    public static readonly (FeteStage Stage, string Name, int GoldCount)[] Stages =
    [
        (FeteStage.Twist, "A Twist of Fête", 6),
        (FeteStage.SofterStuff, "Made of Softer Stuff", 3),
        (FeteStage.ShearYak, "Shear-a-Yak", 4),
        (FeteStage.ToyHunter, "Toy Hunter", 4),
        (FeteStage.Presents, "Presents of Mind", 6),
    ];

    public static (FeteStage Stage, string Name, int GoldCount)? Find(string name)
    {
        foreach (var item in Stages)
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }

    public static (FeteStage Stage, string Name, int GoldCount)? Find(ushort fateId) => fateId switch
    {
        1712 => Stages[0],
        1713 => Stages[1],
        1714 => Stages[2],
        1715 => Stages[3],
        1716 => Stages[4],
        _ => null,
    };

    public static FeteStage Next(FeteStage stage) => stage switch
    {
        FeteStage.Twist => FeteStage.SofterStuff,
        FeteStage.SofterStuff => FeteStage.ShearYak,
        FeteStage.ShearYak => FeteStage.ToyHunter,
        FeteStage.ToyHunter => FeteStage.Presents,
        _ => FeteStage.Twist,
    };

    // Player supplied stage locations. FATE center/radius is too broad to use
    // as a route waypoint; the controller uses these provisional positions.
    public static Vector3? ProvisionalWaypoint(FeteStage stage) => stage switch
    {
        // fete-capture-20261007-100000.jsonl
        FeteStage.Twist or FeteStage.ShearYak => new Vector3(10.060686f, 0.29999006f, -145.31203f),
        // fete-capture-20261007-100253.jsonl: second, toy-making Fête
        FeteStage.SofterStuff => new Vector3(36.96617f, -19.90003f, 87.32608f),
        // fete-capture-20261007-100444.jsonl: fourth Fête
        FeteStage.ToyHunter => new Vector3(-76.354965f, 0f, -0.04517188f),
        // fete-capture-20261007-100345.jsonl: fifth Fête
        FeteStage.Presents => new Vector3(50.036926f, -19.95f, 37.29581f),
        _ => null,
    };
}
