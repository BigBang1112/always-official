namespace AlwaysOfficial.Tests;

public class HeldButtonsTests
{
    [Test]
    public async Task ResetSamplesDoNotActLikeReleases()
    {
        var held = new HeldButtons();
        held.Remember(true, true);
        for (var frame = 0; frame < 300; frame++)
        {
            await Assert.That(held.Sample(0, 0)).IsEqualTo(1);
            await Assert.That(held.Sample(1, 0)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task RealReleasesAreIndependent()
    {
        var held = new HeldButtons();
        held.Remember(true, true);
        held.Release(0);
        await Assert.That(held.Sample(0, 0)).IsEqualTo(0);
        await Assert.That(held.Sample(1, 0)).IsEqualTo(1);
        held.Release(1);
        await Assert.That(held.Mask).IsEqualTo((byte)0);
        await Assert.That(held.Sample(0, 128)).IsEqualTo(128);
    }

    [Test]
    public async Task FocusOrSessionResetForgetsRememberedButtons()
    {
        var held = new HeldButtons();
        held.Remember(true, true);
        held.Reset();
        await Assert.That(held.Sample(0, 0)).IsEqualTo(0);
        await Assert.That(held.Sample(1, 0)).IsEqualTo(0);
    }
}
