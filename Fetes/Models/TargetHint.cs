namespace AutoFete;

public readonly record struct TargetHint(string Sheet, uint RowId, string Name)
{
    public static TargetHint Npc(uint rowId, string name) => new("BNpcName", rowId, name);
    public static TargetHint Interactable(uint rowId, string name) => new("EObjName", rowId, name);

    public static readonly TargetHint MightyMoogle = Npc(10080, "Mighty Moogle");
    public static readonly TargetHint ChirpyChocobo = Npc(10081, "Chirpy Chocobo");
    public static readonly TargetHint PerkyPiggy = Npc(10082, "Perky Piggy");
}
