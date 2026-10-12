namespace AutoFete;

internal sealed class FetePresentOpener(GameActions actions)
{
    public const uint PresentItemId = 33441;

    private DateTime startedUtc;
    private DateTime nextAttemptUtc;
    private int countBeforeUse;
    private int failedAttempts;
    private int opened;
    private int blockedCount = -1;
    private bool waitingForConsumption;

    public bool IsActive { get; private set; }
    public string Status { get; private set; } = "";

    public bool ShouldStart(int count) => count > 0 && count != blockedCount;

    public void Start(bool waitForRewards = false)
    {
        IsActive = true;
        startedUtc = DateTime.UtcNow;
        nextAttemptUtc = waitForRewards ? startedUtc.AddSeconds(3) : startedUtc;
        countBeforeUse = 0;
        failedAttempts = 0;
        opened = 0;
        waitingForConsumption = false;
        blockedCount = -1;
        Status = waitForRewards ? StatusText.Get("Progress_WaitingForRewards") : StatusText.Get("Progress_OpeningPresents");
    }

    public void Reset()
    {
        IsActive = false;
        Status = "";
        waitingForConsumption = false;
        blockedCount = -1;
    }

    public bool Tick()
    {
        if (!IsActive)
            return true;

        var now = DateTime.UtcNow;
        var count = GameActions.GetInventoryItemCount(PresentItemId);
        if (count < 0)
        {
            Status = string.Empty;
            return FinishIfTimedOut(now);
        }

        if (waitingForConsumption && count < countBeforeUse)
        {
            opened += countBeforeUse - count;
            waitingForConsumption = false;
            failedAttempts = 0;
            nextAttemptUtc = now.AddSeconds(1);
        }

        if (now < nextAttemptUtc)
        {
            Status = StatusText.Get("Progress_OpeningPresents");
            return false;
        }

        if (count == 0)
        {
            Complete(StatusText.Format("Result_PresentsOpened", opened));
            return true;
        }

        if (now - startedUtc > TimeSpan.FromMinutes(2) || failedAttempts >= 3)
        {
            Complete(StatusText.Format("Error_PresentsRemain", count), count);
            return true;
        }

        if (!actions.CanUseInventoryItem)
        {
            Status = string.Empty;
            return false;
        }

        if (GameActions.GetFreeInventorySlots() == 0)
        {
            Complete(StatusText.Format("Error_InventoryFull", count), count);
            return true;
        }

        countBeforeUse = count;
        waitingForConsumption = GameActions.TryUseInventoryItem(PresentItemId);
        failedAttempts++;
        nextAttemptUtc = now.AddSeconds(waitingForConsumption ? 4 : 2);
        Status = StatusText.Get("Progress_OpeningPresents");
        return false;
    }

    private bool FinishIfTimedOut(DateTime now)
    {
        if (now - startedUtc <= TimeSpan.FromMinutes(2))
            return false;
        Complete(StatusText.Get("Error_InventoryUnavailable"));
        return true;
    }

    private void Complete(string status, int remaining = 0)
    {
        IsActive = false;
        Status = status;
        if (remaining > 0)
            blockedCount = remaining;
    }
}
