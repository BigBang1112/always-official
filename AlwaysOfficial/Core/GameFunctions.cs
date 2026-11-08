namespace AlwaysOfficial.Core;

internal static unsafe class GameFunctions
{
    internal const uint GhostStartRecordAddress = 0x005dfe90;
    internal const uint StartOfficialRecordAddress = 0x004bbb70;
    internal const uint GhostDestroyAddress = 0x005e0250;
    internal const uint WriteArchiveMaskAddress = 0x00909e60;
    internal const uint WriteArchiveDataAddress = 0x00909a60;
    internal const uint ChangeRaceStateAddress = 0x004ae9d0;
    internal const uint GetLocalPlayerInfoAddress = 0x005ddb50;

    public static delegate* unmanaged[Thiscall]<nint, uint, int, void> ChangeRaceState =>
        (delegate* unmanaged[Thiscall]<nint, uint, int, void>)Memory.Rebase(ChangeRaceStateAddress);
    public static delegate* unmanaged[Thiscall]<nint, nint> GetLocalPlayerInfo =>
        (delegate* unmanaged[Thiscall]<nint, nint>)Memory.Rebase(GetLocalPlayerInfoAddress);

    public static delegate* unmanaged[Thiscall]<nint, void> StartOfficialRecord =>
        (delegate* unmanaged[Thiscall]<nint, void>)Memory.Rebase(StartOfficialRecordAddress);

    public static delegate* unmanaged[Thiscall]<nint, nint, uint, void> GhostStartRecord =>
        (delegate* unmanaged[Thiscall]<nint, nint, uint, void>)Memory.Rebase(GhostStartRecordAddress);
    public static delegate* unmanaged[Thiscall]<nint, void> GhostDestroy =>
        (delegate* unmanaged[Thiscall]<nint, void>)Memory.Rebase(GhostDestroyAddress);
    public static delegate* unmanaged[Thiscall]<nint, uint*, uint, void> WriteArchiveMask =>
        (delegate* unmanaged[Thiscall]<nint, uint*, uint, void>)Memory.Rebase(WriteArchiveMaskAddress);
    public static delegate* unmanaged[Thiscall]<nint, byte*, uint, void> WriteArchiveData =>
        (delegate* unmanaged[Thiscall]<nint, byte*, uint, void>)Memory.Rebase(WriteArchiveDataAddress);

    internal const uint ChallengeMainLoopAddress = 0x00491080;
    internal const uint RunAddress = 0x0047af80;
    internal const uint ChallengeResultAddress = 0x004d0270;
    internal const uint RequestOfficialRaceAddress = 0x00490e00;
    internal const uint ConfirmOfficialAddress = 0x0062da90;
    internal const uint SetInputStateAddress = 0x008f6f50;
    internal const uint InsertInputEventAddress = 0x008f6fc0;

    public static delegate* unmanaged[Thiscall]<nint, nint*, void> ChallengeMainLoop =>
        (delegate* unmanaged[Thiscall]<nint, nint*, void>)Memory.Rebase(ChallengeMainLoopAddress);

    // CTrackManiaRace::Run yields its status through the third argument
    public static delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void> Run =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Memory.Rebase(RunAddress);

    // CTrackManiaMenus::DialogChallengeResult writes the menu choice at menus+0x700
    public static delegate* unmanaged[Thiscall]<nint, nint*, int, void> ChallengeResult =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int, void>)Memory.Rebase(ChallengeResultAddress);

    // CTrackMania::RequestOfficialRace(context, resetPlayer, custom)
    // custom selects RequestCustomOfficialRace, otherwise it requests StartOfficialRecord
    public static delegate* unmanaged[Thiscall]<nint, nint*, int, int, void> RequestOfficialRace =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Memory.Rebase(RequestOfficialRaceAddress);

    // CGameCtnMenus::DialogConfirmOfficial(allowed, cost, balance, title, message)
    // title/message point to CFastStringInt objects, this only presents the request UI
    public static delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void> ConfirmOfficial =>
        (delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)Memory.Rebase(ConfirmOfficialAddress);

    public static delegate* unmanaged[Thiscall]<nint, void> CancelOfficialRecord =>
        (delegate* unmanaged[Thiscall]<nint, void>)Memory.Rebase(0x004bcc60);

    // GetActionState returns a shared SInputEvent buffer, read its value before calling again
    public static delegate* unmanaged[Thiscall]<nint, nint, nint> GetActionState =>
        (delegate* unmanaged[Thiscall]<nint, nint, nint>)Memory.Rebase(0x008f8160);

    public static delegate* unmanaged[Thiscall]<nint, nint, uint, int> SetInputState =>
        (delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(SetInputStateAddress);

    public static delegate* unmanaged[Thiscall]<nint, nint, uint, int> InsertInputEvent =>
        (delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Memory.Rebase(InsertInputEventAddress);
}
