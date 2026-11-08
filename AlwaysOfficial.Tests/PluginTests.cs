using AlwaysOfficial.Core;

namespace AlwaysOfficial.Tests;

public class PluginTests
{
    private static OfficialSession Armed(int custom = 0)
    {
        var session = new OfficialSession();
        session.Observe(1, 2, 3, true, custom);
        return session;
    }

    [Test]
    [DisplayName("Practice stays practice and first confirmation stays visible")]
    public async Task PracticeKeepsManualConfirmation()
    {
        var s = new OfficialSession();
        s.Observe(1, 2, 3, false, 0);
        await Assert.That(s.RaceStatus(3)).IsEqualTo(3);
        await Assert.That(s.ResultChoice(0x1f)).IsEqualTo(0x1f);
        await Assert.That(s.SkipConfirmation(true)).IsFalse();
    }

    [Test]
    [DisplayName("Restart remains official after the game clears its official flag")]
    public async Task RestartRemainsOfficialAfterFlagClears()
    {
        var s = Armed();
        s.Observe(1, 2, 3, false, 0);
        await Assert.That(s.RaceStatus(3)).IsEqualTo(13);
        await Assert.That(s.SkipConfirmation(true)).IsTrue();
    }

    [Test]
    [DisplayName("Repeated restarts each start a new automatic request")]
    public async Task RepeatedRestartsStartNewRequests()
    {
        var s = Armed();

        for (var i = 0; i < 100; i++)
        {
            await Assert.That(s.RaceStatus(3)).IsEqualTo(13);
            await Assert.That(s.AutomaticRequest).IsTrue();
            s.RequestCompleted(true);
            await Assert.That(s.SkipConfirmation(true)).IsFalse();
            await Assert.That(s.Armed).IsTrue();
        }
    }

    [Test]
    [DisplayName("Second reset status follows the same official path")]
    public async Task SecondResetStatusRequestsOfficialMode()
    {
        await Assert.That(Armed().RaceStatus(4)).IsEqualTo(13);
    }

    [Test]
    [DisplayName("Finish and result Retry select official retry")]
    public async Task ResultRetryRequestsOfficialMode()
    {
        var s = Armed();
        await Assert.That(s.RaceStatus(1)).IsEqualTo(1);
        await Assert.That(s.ResultChoice(0x1f)).IsEqualTo(0x6d);
        await Assert.That(s.AutomaticRequest).IsTrue();
    }

    [Test]
    [DisplayName("Pause and resume retain the session; pause Retry becomes official")]
    public async Task PauseAndResumePreserveActivation()
    {
        var s = Armed();
        await Assert.That(s.RaceStatus(15)).IsEqualTo(15);
        await Assert.That(s.RaceStatus(0)).IsEqualTo(0);
        await Assert.That(s.ResultChoice(0x1f)).IsEqualTo(0x6d);
    }

    [Test]
    [DisplayName("Unrelated result choices stay unchanged")]
    public async Task UnrelatedResultChoicesRemainUnchanged()
    {
        var s = Armed();
        await Assert.That(s.ResultChoice(0x22)).IsEqualTo(0x22);
        await Assert.That(s.AutomaticRequest).IsFalse();
    }

    [Test]
    [DisplayName("Custom official mode survives both retry paths")]
    public async Task CustomOfficialModeSurvivesBothRetryPaths()
    {
        var s = Armed(1);
        s.Observe(1, 2, 3, false, 0);
        await Assert.That(s.ResultChoice(0x1f)).IsEqualTo(0x6e);
        await Assert.That(s.Custom).IsEqualTo(1);
        s.RequestCompleted(true);
        await Assert.That(s.RaceStatus(3)).IsEqualTo(13);
        await Assert.That(s.Custom).IsEqualTo(1);
    }

    [Test]
    [DisplayName("Denied requests never bypass the confirmation")]
    public async Task DeniedRequestsKeepConfirmation()
    {
        var s = Armed();
        s.RaceStatus(3);
        await Assert.That(s.SkipConfirmation(false)).IsFalse();
    }

    [Test]
    [DisplayName("Failed or cancelled requests disarm without a retry loop")]
    public async Task FailedRequestsDisarmAutomaticRetries()
    {
        var s = Armed();
        s.RaceStatus(3);
        s.RequestCompleted(false);
        await Assert.That(s.RaceStatus(3)).IsEqualTo(3);
        await Assert.That(s.Armed).IsFalse();
    }

    [Test]
    [DisplayName("Changing app, race, or track clears activation")]
    public async Task ChangedOrMissingIdentityClearsActivation()
    {
        foreach (var identity in new[] { (4, 2, 3), (1, 4, 3), (1, 2, 4), (0, 2, 3), (1, 0, 3), (1, 2, 0) })
        {
            var s = Armed();
            s.RaceStatus(3);
            s.Observe(identity.Item1, identity.Item2, identity.Item3, false, 0);
            await Assert.That(s.Armed).IsFalse();
            await Assert.That(s.AutomaticRequest).IsFalse();
        }
    }

    [Test]
    [DisplayName("Leaving a challenge clears even a reused identity")]
    public async Task ChallengeExitClearsReusedIdentity()
    {
        var s = Armed();
        s.Reset();
        s.Observe(1, 2, 3, false, 0);
        await Assert.That(s.RaceStatus(3)).IsEqualTo(3);
    }

    [Test]
    [DisplayName("Other race modes disarm")]
    public async Task OtherRaceModesDisarm()
    {
        var s = Armed();
        await Assert.That(s.RaceStatus(2)).IsEqualTo(2);
        await Assert.That(s.Armed).IsFalse();
    }

    [Test]
    [DisplayName("Real Ghidra CALL bytes resolve to the expected targets")]
    public async Task CallValidationRejectsUnexpectedInstructions()
    {
        await Assert.That(CallPatch.Matches(Convert.FromHexString("E8B393FEFF"), 0x00491bc8, 0x0047af80)).IsTrue();
        await Assert.That(CallPatch.Matches(Convert.FromHexString("E8D1271A00"), 0x0048b2ba, 0x0062da90)).IsTrue();
        await Assert.That(CallPatch.Matches(Convert.FromHexString("E8B393FEFF"), 0x00491bc8, 0x0047af81)).IsFalse();
        await Assert.That(CallPatch.Matches(Convert.FromHexString("E9B393FEFF"), 0x00491bc8, 0x0047af80)).IsFalse();
        await Assert.That(CallPatch.Matches([0xe8], 0x00491bc8, 0x0047af80)).IsFalse();
    }

    [Test]
    [DisplayName("Verified official and input CALL sites resolve in both United and Nations")]
    public async Task VerifiedUnitedAndNationsCallsResolve()
    {
        // Each sequence was read independently from both Ghidra programs, not inferred by an offset.
        foreach (var (site, target, hex) in new (uint, uint, string)[]
        {
            (0x0049cdaf, 0x00491080, "E8CC42FFFF"),
            (0x00491bc8, 0x0047af80, "E8B393FEFF"),
            (0x0049142e, 0x004d0270, "E83DEE0300"),
            (0x004914fb, 0x004d0270, "E870ED0300"),
            (0x004915d3, 0x00490e00, "E828F8FFFF"),
            (0x00491e8b, 0x00490e00, "E870EFFFFF"),
            (0x004927c6, 0x00490e00, "E835E6FFFF"),
            (0x0048b2ba, 0x0062da90, "E8D1271A00"),
            (0x008f914a, 0x008f6f50, "E801DEFFFF"),
            (0x008fc266, 0x008f6fc0, "E855ADFFFF"),
            (0x005dd61f, 0x005dfe90, "E86C280000"),
            (0x005e0cd3, 0x005e0250, "E878F5FFFF"),
            (0x009242f2, 0x00909e60, "E8695BFEFF"),
            (0x0047dc30, 0x004bbb70, "E83BDF0300"),
            (0x0047c2c6, 0x004ae9d0, "E805270300"),
            (0x00480e8f, 0x004ae9d0, "E83CDB0200"),
        })
        {
            await Assert.That(CallPatch.Matches(Convert.FromHexString(hex), site, target)).IsTrue();
            await Assert.That(Convert.ToHexString(CallPatch.Encode(site, target))).IsEqualTo(hex);
        }
    }

    [Test]
    [DisplayName("CALL encoding handles rebasing and x86 address wraparound")]
    public async Task CallEncodingHandlesRebasingAndWraparound()
    {
        foreach (var (site, target) in new (uint, uint)[] { (0x491bc8, 0x47af80), (0x10491bc8, 0x1047af80), (0x491bc8, 0xf0000000), (0xfffffff0, 0x100) })
        {
            await Assert.That(CallPatch.Matches(CallPatch.Encode(site, target), site, target)).IsTrue();
        }
    }
}

