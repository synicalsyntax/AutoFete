namespace AutoFete;

public readonly record struct FeteAction(uint Id, string Name, FeteStage Stage, string TargetRole);
