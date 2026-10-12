using Dalamud.Game;
using System.Globalization;
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace AutoFete;

internal static class StatusText
{
    private static readonly ResourceManager Resources = new(typeof(StatusText));
    private static CultureInfo culture = CultureInfo.GetCultureInfo("en");

    public static void SetLanguage(ClientLanguage language) => culture = language switch
    {
        ClientLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
        ClientLanguage.German => CultureInfo.GetCultureInfo("de"),
        ClientLanguage.French => CultureInfo.GetCultureInfo("fr"),
        _ => CultureInfo.GetCultureInfo("en"),
    };

    public static string Get(string key) =>
        Resources.GetString(key, culture)
        ?? throw new MissingManifestResourceException($"Missing status resource: {key}");

    public static string Format(string key, params object?[] args) =>
        string.Format(culture, Get(key), args);

    public static string ActionTargetRole(string role) => role switch
    {
        "mighty-moogle" => Get("Target_MightyMoogle"),
        "chirpy-chocobo" => Get("Target_ChirpyChocobo"),
        "perky-piggy" => Get("Target_PerkyPiggy"),
        "wooly-yak" => Get("Target_WoolyYak"),
        _ => role,
    };
}
