using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial.Tests;

public class Win32TimeTests
{
    [Test]
    public async Task NativeLocalTimeMatchesManagedLocalTime()
    {
        var before = DateTime.Now;
        Win32.GetLocalTime(out var time);
        var after = DateTime.Now;

        var timestamp = new DateTime(time.Year, time.Month, time.Day,
            time.Hour, time.Minute, time.Second, time.Milliseconds);
        await Assert.That(Marshal.SizeOf<Win32.SYSTEMTIME>()).IsEqualTo(16);
        // GetLocalTime can use a coarser clock than DateTime.Now
        await Assert.That(timestamp >= before.AddSeconds(-1) && timestamp <= after.AddSeconds(1)).IsTrue();
    }
}
