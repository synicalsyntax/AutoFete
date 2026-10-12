using System.Text.Json;

namespace AutoFete;

public sealed class Configuration
{
    public RunMode Mode { get; set; } = RunMode.OneSequence;
    public bool DebugEnabled { get; set; }
    public bool OpenFetePresents { get; set; }
    public bool ContinueFeteActionsAfterFullCredit { get; set; }

    public static Configuration Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path)) ?? new Configuration()
        : new Configuration();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Mode, OpenFetePresents,
            ContinueFeteActionsAfterFullCredit, DebugEnabled },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

public enum RunMode
{
    OneSequence,
    RemainingCycle,
}
