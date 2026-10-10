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
        Status = waitForRewards ? "Waiting for Fête rewards." : "Opening Fête presents in inventory.";
        startedUtc = DateTime.UtcNow;
        nextAttemptUtc = waitForRewards ? startedUtc.AddSeconds(3) : startedUtc;
        countBeforeUse = 0;
        failedAttempts = 0;
        opened = 0;
        waitingForConsumption = false;
        blockedCount = -1;
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
            Status = "Waiting for inventory to become available.";
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
            Status = waitingForConsumption
                ? $"Opening Fête presents: {opened} opened; waiting for item use."
                : $"Opening Fête presents: {opened} opened; waiting for rewards.";
            return false;
        }

        if (count == 0)
        {
            Complete($"Fête presents opened: {opened}.");
            return true;
        }

        if (now - startedUtc > TimeSpan.FromMinutes(2) || failedAttempts >= 3)
        {
            Complete($"Could not open all Fête presents; {count} remain in inventory.", count);
            return true;
        }

        if (!actions.CanUseInventoryItem)
        {
            Status = "Waiting to use Fête presents.";
            return false;
        }

        if (GameActions.GetFreeInventorySlots() == 0)
        {
            Complete($"Could not open Fête presents; inventory is full ({count} remain).", count);
            return true;
        }

        countBeforeUse = count;
        waitingForConsumption = GameActions.TryUseInventoryItem(PresentItemId);
        failedAttempts++;
        nextAttemptUtc = now.AddSeconds(waitingForConsumption ? 4 : 2);
        Status = waitingForConsumption
            ? $"Opening Fête presents: {opened} opened; waiting for item use."
            : $"Fête present use was rejected; retrying ({failedAttempts}/3).";
        return false;
    }

    private bool FinishIfTimedOut(DateTime now)
    {
        if (now - startedUtc <= TimeSpan.FromMinutes(2))
            return false;
        Complete("Could not open Fête presents; inventory was unavailable.");
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
