namespace AlwaysOfficial;

internal sealed class OfficialSession
{
    private (nint App, nint Race, nint Challenge) observedIdentity;

    public bool Armed { get; private set; }
    public bool AutomaticRequest { get; private set; }
    public int Custom { get; private set; }

    public void Observe(nint app, nint race, nint challenge, bool official, int custom)
    {
        var identity = (app, race, challenge);

        if (observedIdentity != identity)
        {
            Reset();
            observedIdentity = identity;
        }

        if (app == 0 || race == 0 || challenge == 0)
        {
            Reset();
            return;
        }

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
            return 13; // CGameRace::EStatus: request an official run
        }

        // 15/16 enter the quit/pause dialog
        // choosing Resume must preserve the session
        if (status is not (0 or 1 or 3 or 4 or 13 or 15 or 16))
        {
            Reset();
        }

        return status;
    }

    public int ResultChoice(int choice)
    {
        if (Armed && choice == 0x1f)
        {
            AutomaticRequest = true;
            return Custom != 0 ? 0x6e : 0x6d;
        }

        return choice;
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
