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

    private static nint Action(int button)
    {
        return Memory.ReadField<nint>((nint)Memory.Rebase(0x00cd3f64), button * 4);
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

        var port = Memory.ReadField<nint>(race, 0x20);
        if (port == 0)
        {
            Reset();
            return;
        }

        // CInputPort::GetActionState queries the embedded store at port+0x40 with time -1.
        // Its result is a shared static SInputEvent buffer, so read each value before calling again.
        var accelerate = held.Sample(0, Memory.ReadField<int>(GameFunctions.GetActionState(port, Action(0)), 4)) != 0;
        var brake = held.Sample(1, Memory.ReadField<int>(GameFunctions.GetActionState(port, Action(1)), 4)) != 0;

        inputOwner = (app, race, challenge, port, Memory.ReadField<nint>(race, 0xc8));
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

        var count = Memory.ReadField<uint>(inputOwner.Map, 0x2c);

        if (count > 4096)
        {
            throw new InvalidOperationException("Unexpected input binding count");
        }

        var bindings = Memory.ReadField<nint>(inputOwner.Map, 0x30);
        var deviceCount = Memory.ReadField<uint>(inputOwner.Port, 0x2c);
        var devices = Memory.ReadField<nint>(inputOwner.Port, 0x30);

        for (uint i = 0; i < count; i++)
        {
            var action = Memory.ReadField<nint>(bindings, (int)i * 8);
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

            var device = Memory.ReadField<nint>(devices, index * 4);
            var vtable = Memory.ReadField<nint>(device, 0);

            if (((delegate* unmanaged[Thiscall]<nint, int>)Memory.ReadField<nint>(vtable, 0x78))(device) != 0)
            {
                continue;
            }

            var obj = Memory.ReadField<uint>(bindings, (int)i * 8 + 4);

            if (obj >= 0x90)
            {
                throw new InvalidOperationException("Unexpected keyboard object index");
            }

            // Keyboard objects use this game's DIK table, not Windows virtual key indices
            var scan = Memory.ReadField<uint>((nint)Memory.Rebase(0x00d219e0), (int)obj * 0x20c);

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
            var core = Memory.ReadField<nint>((nint)Memory.Rebase(0x00d731e0), 0);
            var timer = Memory.ReadField<nint>(core, 0x14);
            var tick = Memory.ReadField<uint>(timer != 0 ? timer : core + 0xa0, 0x1c);
            InputEvent released = new()
            {
                Action = Action(button),
                Value = 0
            };

            // CInputEventsStore::SetState inserts only when the converted value differs
            // from the stored state at this time. Inject into the same store as the sampler.
            GameFunctions.SetInputState(inputOwner.Port + 0x40, (nint)(&released), tick);
            Log.Write($"Remembered {(button == 0 ? "accelerator" : "brake")} released by keyboard state.");
        }
    }

    private static bool Owns(nint store)
    {
        if (held.Mask == 0 || inputOwner.Port == 0 || store != inputOwner.Port + 0x40)
        {
            return false;
        }

        if (!OfficialHooks.InputsEnabled || Memory.ReadField<nint>(inputOwner.App, 0x454) != inputOwner.Race ||
            Memory.ReadField<nint>(inputOwner.App, 0x198) != inputOwner.Challenge || Memory.ReadField<nint>(inputOwner.Race, 0x20) != inputOwner.Port ||
            (Memory.ReadField<int>(inputOwner.Port, 0x90) == 0 && Memory.ReadField<int>(inputOwner.Port, 0x98) == 0) ||
            (!OfficialHooks.AutomaticInputTransition && (Memory.ReadField<int>(inputOwner.Race, 0x48) != 0 ||
                Memory.ReadField<nint>(inputOwner.Port, 0x20) != inputOwner.Map)))
        {
            Reset();
            return false;
        }

        return true;
    }

    private static int DeviceFor(nint map, uint binding)
    {
        var count = Memory.ReadField<uint>(map, 0x20);

        if (count > 64)
        {
            throw new InvalidOperationException("Unexpected input device count");
        }

        var categories = Memory.ReadField<nint>(map, 0x24);

        for (var i = 0; i < count; i++)
        {
            if (binding < Memory.ReadField<uint>(categories, i * 8) + Memory.ReadField<uint>(categories, i * 8 + 4))
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

        var map = Memory.ReadField<nint>(inputOwner.Port, 0x20);

        if (map == 0 || map == inputOwner.Map)
        {
            return false;
        }

        uint count = Memory.ReadField<uint>(map, 0x2c), originalCount = Memory.ReadField<uint>(inputOwner.Map, 0x2c);

        if (count > 4096 || originalCount > 4096)
        {
            throw new InvalidOperationException("Unexpected input binding count");
        }

        nint bindings = Memory.ReadField<nint>(map, 0x30), original = Memory.ReadField<nint>(inputOwner.Map, 0x30);

        for (uint i = 0; i < count; i++)
        {
            if (Memory.ReadField<nint>(bindings, (int)i * 8) != action)
            {
                continue;
            }

            var device = DeviceFor(map, i);

            if (device < 0)
            {
                continue;
            }

            var obj = Memory.ReadField<uint>(bindings, (int)i * 8 + 4);

            for (uint j = 0; j < originalCount; j++)
            {
                if (Memory.ReadField<nint>(original, (int)j * 8) == Action(button) &&
                    Memory.ReadField<uint>(original, (int)j * 8 + 4) == obj && DeviceFor(inputOwner.Map, j) == device)
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
        // CALL 0x008f914a in CInputPort::ReadCurMapLatestEventsFromHarware -> SetState.
        // Preserve remembered accelerator/brake values when restart sampling clears them.
        try
        {
            PollKeyboard();

            if (Owns(store))
            {
                var value = Memory.ReadField<InputEvent>(inputEvent, 0);

                for (var i = 0; i < 2; i++)
                {
                    if (value.Action == Action(i))
                    {
                        value.Value = held.Sample(i, value.Value);

                        if (value.Value != 0 && Memory.ReadField<int>(inputEvent, 4) == 0)
                        {
                            ReplayMetadata.Carried(i);
                        }

                        return GameFunctions.SetInputState(store, (nint)(&value), time);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Reset();
            try
            {
                Log.Error("Remembered input disabled", e);
            }
            catch { } // Logging must not escape the native callback
        }

        return GameFunctions.SetInputState(store, inputEvent, time);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    public static int ButtonEventDetour(nint store, nint inputEvent, uint time)
    {
        // CALL 0x008fc266 in CInputPortDx8::GatherLatestInputs -> InsertSorted
        // Unlike SetState, this converts and inserts without checking for a changed value
        // The site also handles non-button events, only matching zero-valued releases matter here
        try
        {
            // This CALL is in DirectInput's buffered-event path, not ClearInputs sampler
            if (Owns(store) && Memory.ReadField<int>(inputEvent, 4) == 0)
            {
                for (var i = 0; i < 2; i++)
                {
                    if (IsReleaseFor(Memory.ReadField<nint>(inputEvent, 0), i))
                    {
                        held.Release(i);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Reset();
            try
            {
                Log.Error("Remembered input disabled", e);
            }
            catch { } // Logging must not escape the native callback
        }

        return GameFunctions.InsertInputEvent(store, inputEvent, time);
    }
}
