using Dalamud.Game.ClientState.Objects.Enums;
using DalamudFateState = Dalamud.Game.ClientState.Fates.FateState;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Numerics;
using System.Text.Json;

namespace AutoFete;

internal sealed unsafe class CaptureRecorder(IClientState clientState, IObjectTable objects, IFateTable fates,
    ITargetManager targets, FeteController controller, GameActions actions)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    private readonly List<string> samples = [];
    private DateTime nextSampleUtc;
    private string? lastPayload;

    public bool IsRecording { get; private set; }
    public int SampleCount => samples.Count;

    public void Start()
    {
        samples.Clear();
        lastPayload = null;
        nextSampleUtc = DateTime.MinValue;
        IsRecording = true;
    }

    public string Stop(string directory)
    {
        IsRecording = false;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"fete-capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        File.WriteAllLines(path, samples);
        samples.Clear();
        lastPayload = null;
        return path;
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (!IsRecording || now < nextSampleUtc || samples.Count >= 10000)
            return;

        nextSampleUtc = now.AddMilliseconds(250);
        var player = objects.LocalPlayer;
        if (player == null || clientState.TerritoryType != FeteCatalog.FirmamentTerritoryId)
            return;

        var selectedTarget = targets.Target;
        var playerState = PlayerState.Instance();
        var nearby = objects
            .Where(x => x.ObjectKind != ObjectKind.Pc && Vector3.Distance(player.Position, x.Position) < 40f)
            .Select(x => new
            {
                x.BaseId,
                NameId = ReadNameId(x),
                x.GameObjectId,
                Name = x.Name.ToString(),
                Kind = x.ObjectKind.ToString(),
                x.IsTargetable,
                x.Position,
                x.Rotation,
                EmoteId = ReadEmoteId(x),
                Statuses = x is IBattleChara c ? ReadStatuses(c) : [],
            })
            .OrderBy(x => x.BaseId)
            .ThenBy(x => x.GameObjectId)
            .ToArray();

        var fateManager = FateManager.Instance();
        var fateRows = FeteCatalog.WithDefinitions(fates);
        var activeFete = fateRows.FirstOrDefault(x => x.Stage != null &&
            (x.Fate.State == DalamudFateState.Running ||
             x.Fate.State == DalamudFateState.Ending && controller.CurrentStage == x.Stage.Value.Stage));
        var nativeFates = fateRows.Select(row =>
        {
            var f = row.Fate;
            var native = fateManager == null ? null : fateManager->GetFateById(f.FateId);
            return new
            {
                f.FateId,
                ObjectiveNpc = native == null ? 0u : native->ObjectiveNpc,
                EventItem = native == null ? 0u : native->EventItem,
                TurnInEventItem = native == null ? 0u : native->TurnInEventItem,
                GivenStatus = native == null ? 0 : native->GivenStatus,
                StateFlags = native == null ? 0 : native->StateFlags,
            };
        }).ToArray();

        var snapshot = new
        {
            Territory = clientState.TerritoryType,
            Job = player.ClassJob.RowId,
            player.Position,
            TargetObjectId = selectedTarget?.GameObjectId,
            TargetBaseId = selectedTarget?.BaseId,
            TargetNameId = ReadNameId(selectedTarget),
            TargetName = selectedTarget?.Name.ToString(),
            ActiveFeteId = activeFete.Fate?.FateId,
            ActiveFeteStage = activeFete.Stage?.Stage.ToString(),
            JoinConfirmed = activeFete.Fate != null && actions.IsJoined(activeFete.Fate.FateId),
            ControllerArmed = controller.IsArmed,
            ControllerStage = controller.CurrentStage?.ToString(),
            ControllerStatus = controller.Status,
            actions.LastJoinAttempt,
            CurrentFateId = fateManager == null ? 0 : fateManager->GetCurrentFateId(),
            FateJoined = fateManager != null && fateManager->FateJoined != 0,
            SyncedFateId = fateManager == null ? 0 : fateManager->SyncedFateId,
            PlayerIsLevelSynced = playerState != null && playerState->IsLevelSynced,
            YakFleeceCount = GameActions.GetKeyItemCount(ShearYakFete.YakFleeceEventItemId),
            LuckyToyCount = GameActions.GetKeyItemCount(ToyHunterFete.LuckyToyEventItemId),
            FetePresentCount = GameActions.GetInventoryItemCount(FetePresentOpener.PresentItemId),
            actions.CarryingObject,
            actions.CarryingItem,
            PlayerEmoteId = ReadEmoteId(player),
            Statuses = ReadStatuses(player),
            Fates = fateRows.Select(row =>
            {
                var f = row.Fate;
                return new
                {
                    f.FateId,
                    Name = f.Name.ToString(),
                    State = f.State.ToString(),
                    f.HandInCount,
                    f.Progress,
                    f.TimeRemaining,
                    f.Position,
                    f.Radius,
                };
            }).OrderBy(x => x.FateId).ToArray(),
            NativeFates = nativeFates,
            Objects = nearby,
        };

        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (payload == lastPayload)
            return;

        lastPayload = payload;
        samples.Add(JsonSerializer.Serialize(new { Utc = now, Snapshot = snapshot }, JsonOptions));
    }

    private static uint ReadNameId(IGameObject? actor) =>
        actor is ICharacter character ? character.NameId : 0u;

    private static uint[] ReadStatuses(IBattleChara actor) =>
        actor.StatusList.Where(s => s.StatusId != 0).Select(s => s.StatusId).ToArray();

    private static unsafe ushort? ReadEmoteId(IGameObject actor)
    {
        if (actor is not IBattleChara || actor.Address == 0)
            return null;
        return ((Character*)actor.Address)->EmoteController.EmoteId;
    }
}
