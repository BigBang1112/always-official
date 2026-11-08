using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial.Tests;

public class UserDataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadsResolvedUnicodeDirectory(bool fallback)
    {
        const string expected = @"D:\Custom user data\Petrův profil";
        var image = Marshal.AllocHGlobal(0x400);
        var path = Marshal.StringToHGlobalUni(expected);
        try
        {
            unsafe
            {
                new Span<byte>((void*)image, 0x400).Clear();
            }
            var engines = image + 0x100;
            var system = image + 0x200;
            var user = image + 0x300;
            Marshal.WriteInt32(image, 0x20, 12);
            Marshal.WriteIntPtr(image, 0x24, engines);
            Marshal.WriteIntPtr(engines, 11 * 4, system);
            Marshal.WriteIntPtr(system, fallback ? 0x50 : 0x28, user);
            Marshal.WriteInt32(user, 0x40, expected.Length);
            Marshal.WriteIntPtr(user, 0x44, path);
            await Assert.That(UserData.GetDirectory(image)).IsEqualTo(expected);

            // The drive can exist before SetDriveName has supplied its path
            Marshal.WriteInt32(user, 0x40, 0);
            await Assert.That(UserData.GetDirectory(image)).IsNull();
        }
        finally
        {
            Marshal.FreeHGlobal(path);
            Marshal.FreeHGlobal(image);
        }
    }

    [Test]
    public async Task StartupWithoutMainEngineHasNoDirectory()
    {
        await Assert.That(UserData.GetDirectory(0)).IsNull();
    }
}
