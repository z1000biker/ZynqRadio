namespace ZynqRadio.Audio;

public sealed record AudioDeviceDescriptor(
    int Index,
    string Name,
    string Id)
{
    public override string ToString() => Name;
}
