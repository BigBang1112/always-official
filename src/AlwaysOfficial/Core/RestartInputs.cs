using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlwaysOfficial.Core;

// Remember the port's working button state BEFORE the official request clears it
internal static unsafe class RestartInputs
{
    private static readonly HeldButtons held = new();
    private static readonly List<int>[] keyboardKeys = [[], []];
#if NATIVE_TESTS
    internal static delegate* unmanaged[Stdcall]<int, short> TestKeyState;
#endif
    private static (nint App, nint Race, nint Challenge, nint Port, nint Map) inputOwner;

    private struct InputEvent
    {
        public nint Action;
        public int Value;
    }

    private static T Read<T>(nint obj, int offset) where T : unmanaged
    {
        return Memory.TryRead((byte*)obj + offset, out T value) ? value : throw new InvalidOperationException("Input object is unreadable");
    }

    private static nint Action(int button)
    {
        return Read<nint>((nint)Memory.Rebase(0x00cd3f64), button * 4);
    }

    public static void Reset()
    {
        held.Reset();
        inputOwner = default;
        keyboardKeys[0].Clear();
        keyboardKeys[1].Clear();
    }

    public static void Observe(nint app, nint race, nint challenge)
    {
        if (inputOwner != default && (inputOwner.App != app || inputOwner.Race != race || inputOwner.Challenge != challenge))
        {
            Reset();
        }
    }

    public static void Remember(nint app, nint race, nint challenge)
    {
        Observe(app, race, challenge);

        var port = Read<nint>(race, 0x20);
        if (port == 0)
        {
            Reset();
            return;
        }

        var get = (delegate* unmanaged[Thiscall]<nint, nint, nint>)Memory.Rebase(0x008f8160);

        var accelerate = held.Sample(0, Read<int>(get(port, Action(0)), 4)) != 0;
        var brake = held.Sample(1, Read<int>(get(port, Action(1)), 4)) != 0;

        inputOwner = (app, race, challenge, port, Read<nint>(race, 0xc8));
        held.Remember(accelerate, brake);

        RememberKeyboardKeys();

        Log.Write($"Remembered restart buttons: accelerate={accelerate}, brake={brake}.");
    }

    private static bool KeyIsDown(int key)
    {
#if NATIVE_TESTS
        if (TestKeyState != null)
        {
            return (TestKeyState(key) & 0x8000) != 0;
        }
#endif
        return (Win32.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private static void RememberKeyboardKeys()
    {
        keyboardKeys[0].Clear();
        keyboardKeys[1].Clear();

        var count = Read<uint>(inputOwner.Map, 0x2c);

        if (count > 4096)
        {
            throw new InvalidOperationException("Unexpected input binding count");
        }

        var bindings = Read<nint>(inputOwner.Map, 0x30);
        var deviceCount = Read<uint>(inputOwner.Port, 0x2c);
        var devices = Read<nint>(inputOwner.Port, 0x30);

        for (uint i = 0; i < count; i++)
        {
            var action = Read<nint>(bindings, (int)i * 8);
            var button = action == Action(0) ? 0 : action == Action(1) ? 1 : -1;

            if (button < 0 || (held.Mask & (1 << button)) == 0)
            {
                continue;
            }

            var index = DeviceFor(inputOwner.Map, i);

            if (index < 0 || index >= deviceCount)
            {
                continue;
            }

            var device = Read<nint>(devices, index * 4);
            var vtable = Read<nint>(device, 0);

            if (((delegate* unmanaged[Thiscall]<nint, int>)Read<nint>(vtable, 0x78))(device) != 0)
            {
                continue;
            }

            var obj = Read<uint>(bindings, (int)i * 8 + 4);

            if (obj >= 0x90)
            {
                throw new InvalidOperationException("Unexpected keyboard object index");
            }

            // Keyboard objects use this game's DIK table, not Windows virtual key indices
            var scan = Read<uint>((nint)Memory.Rebase(0x00d219e0), (int)obj * 0x20c);

            if ((scan & 0x80) != 0)
            {
                scan = 0xe000 | (scan & 0x7f);
            }

            var key = (int)Win32.MapVirtualKey(scan, 3); // MAPVK_VSC_TO_VK_EX (includes arrows)

            // Only watch physical keys confirmed down when the press was remembered
            // Controller-only and synthetic input continue using buffered release events
            if (key != 0 && KeyIsDown(key) && !keyboardKeys[button].Contains(key))
            {
                keyboardKeys[button].Add(key);
            }
        }
    }

    public static void PollKeyboard()
    {
        if (inputOwner.Port == 0 || !Owns(inputOwner.Port + 0x40))
        {
            return;
        }

        for (var button = 0; button < 2; button++)
        {
            if ((held.Mask & (1 << button)) == 0 || keyboardKeys[button].Count == 0)
            {
                continue;
            }

            var down = false;

            foreach (var key in keyboardKeys[button])
            {
                down |= KeyIsDown(key);
            }

            if (down)
            {
                continue;
            }

            held.Release(button);

            // The buffered key-up may have been discarded during countdown
            // Clear the actual port state too, at the same simulation tick InputRace will query
            var core = Read<nint>((nint)Memory.Rebase(0x00d731e0), 0);
            var timer = Read<nint>(core, 0x14);
            var tick = Read<uint>(timer != 0 ? timer : core + 0xa0, 0x1c);
            InputEvent released = new()
            {
                Action = Action(button),
                Value = 0
            };
            ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(0x008f6f50))(inputOwner.Port + 0x40, (nint)(&released), tick);
            Log.Write($"Remembered {(button == 0 ? "accelerator" : "brake")} released by keyboard state.");
        }
    }

    private static bool Owns(nint store)
    {
        if (held.Mask == 0 || inputOwner.Port == 0 || store != inputOwner.Port + 0x40)
        {
            return false;
        }

        if (!OfficialHooks.InputsEnabled || Read<nint>(inputOwner.App, 0x454) != inputOwner.Race ||
            Read<nint>(inputOwner.App, 0x198) != inputOwner.Challenge || Read<nint>(inputOwner.Race, 0x20) != inputOwner.Port ||
            (Read<int>(inputOwner.Port, 0x90) == 0 && Read<int>(inputOwner.Port, 0x98) == 0) ||
            (!OfficialHooks.AutomaticInputTransition && (Read<int>(inputOwner.Race, 0x48) != 0 ||
                Read<nint>(inputOwner.Port, 0x20) != inputOwner.Map)))
        {
            Reset();
            return false;
        }

        return true;
    }

    private static int DeviceFor(nint map, uint binding)
    {
        var count = Read<uint>(map, 0x20);

        if (count > 64)
        {
            throw new InvalidOperationException("Unexpected input device count");
        }

        var cats = Read<nint>(map, 0x24);

        for (var i = 0; i < count; i++)
        {
            if (binding < Read<uint>(cats, i * 8) + Read<uint>(cats, i * 8 + 4))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsReleaseFor(nint action, int button)
    {
        if (action == Action(button))
        {
            return true;
        }

        var map = Read<nint>(inputOwner.Port, 0x20);

        if (map == 0 || map == inputOwner.Map)
        {
            return false;
        }

        uint count = Read<uint>(map, 0x2c), originalCount = Read<uint>(inputOwner.Map, 0x2c);

        if (count > 4096 || originalCount > 4096)
        {
            throw new InvalidOperationException("Unexpected input binding count");
        }

        nint bindings = Read<nint>(map, 0x30), original = Read<nint>(inputOwner.Map, 0x30);

        for (uint i = 0; i < count; i++)
        {
            if (Read<nint>(bindings, (int)i * 8) != action)
            {
                continue;
            }

            var device = DeviceFor(map, i);

            if (device < 0)
            {
                continue;
            }

            var obj = Read<uint>(bindings, (int)i * 8 + 4);

            for (uint j = 0; j < originalCount; j++)
            {
                if (Read<nint>(original, (int)j * 8) == Action(button) &&
                    Read<uint>(original, (int)j * 8 + 4) == obj && DeviceFor(inputOwner.Map, j) == device)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    public static int SampleDetour(nint store, nint inputEvent, uint time)
    {
        try
        {
            PollKeyboard();

            if (Owns(store))
            {
                var value = Read<InputEvent>(inputEvent, 0);

                for (var i = 0; i < 2; i++)
                {
                    if (value.Action == Action(i))
                    {
                        value.Value = held.Sample(i, value.Value);
                        return ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(0x008f6f50))(store, (nint)(&value), time);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Reset();
            Log.Error("Remembered input disabled", e);
        }

        return ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(0x008f6f50))(store, inputEvent, time);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    public static int ButtonEventDetour(nint store, nint inputEvent, uint time)
    {
        try
        {
            // This CALL is in DirectInput's buffered-event path, not ClearInputs sampler
            if (Owns(store) && Read<int>(inputEvent, 4) == 0)
            {
                for (var i = 0; i < 2; i++)
                {
                    if (IsReleaseFor(Read<nint>(inputEvent, 0), i))
                    {
                        held.Release(i);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Reset();
            Log.Error("Remembered input disabled", e);
        }

        return ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(0x008f6fc0))(store, inputEvent, time);
    }
}
