namespace AlwaysOfficial;

internal sealed class HeldButtons
{
    public byte Mask { get; private set; }

    public void Remember(bool accelerate, bool brake)
    {
        Mask = (byte)((accelerate ? 1 : 0) | (brake ? 2 : 0));
    }

    public void Release(int button)
    {
        Mask &= (byte)~(1 << button);
    }

    public int Sample(int button, int value)
    {
        return (Mask & (1 << button)) != 0 ? 1 : value;
    }

    public void Reset()
    {
        Mask = 0;
    }
}
