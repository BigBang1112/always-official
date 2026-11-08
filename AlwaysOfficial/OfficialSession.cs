namespace AlwaysOfficial;

internal sealed class OfficialSession
{
    private const int RequestOfficialStatus = 13;
    private const int RetryChoice = 0x1f;
    private const int RetryOfficialChoice = 0x6d;
    private const int RetryCustomOfficialChoice = 0x6e;

    private (nint App, nint Race, nint Challenge) observedIdentity;

    public bool Armed { get; private set; }
    public bool AutomaticRequest { get; private set; }
    public int Custom { get; private set; }

    public void Observe(nint app, nint race, nint challenge, bool official, int custom)
    {
        var identity = (app, race, challenge);

        if (app == 0 || race == 0 || challenge == 0)
        {
            Reset();
            observedIdentity = identity;
            return;
        }

        if (observedIdentity != identity)
        {
            Reset();
            observedIdentity = identity;
        }

        // Keep activation and custom mode when the game clears its official flag during a retry.
        if (official)
        {
            Armed = true;
            Custom = custom;
        }
    }

    public int RaceStatus(int status)
    {
        if (Armed && status is 3 or 4)
        {
            AutomaticRequest = true;
            return RequestOfficialStatus;
        }

        // 15/16 enter the quit/pause dialog
        // choosing Resume must preserve the session
        if (status is not (0 or 1 or 3 or 4 or RequestOfficialStatus or 15 or 16))
        {
            Reset();
        }

        return status;
    }

    public int ResultChoice(int choice)
    {
        if (!Armed || choice != RetryChoice)
        {
            return choice;
        }

        AutomaticRequest = true;
        return Custom != 0 ? RetryCustomOfficialChoice : RetryOfficialChoice;
    }

    public bool SkipConfirmation(bool allowed)
    {
        return AutomaticRequest && Armed && allowed;
    }

    public void RequestCompleted(bool official)
    {
        AutomaticRequest = false;

        if (!official)
        {
            Reset();
        }
    }

    public void Reset()
    {
        Armed = AutomaticRequest = false;
        Custom = 0;
    }
}
