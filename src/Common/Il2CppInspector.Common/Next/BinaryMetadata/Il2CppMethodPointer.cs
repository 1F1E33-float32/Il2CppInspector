using VersionedSerialization.Attributes;

namespace Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppMethodPointer
{
    [NativeInteger]
    public ulong Value { get; set; }

    // A positional record parameter would add a second serialized property alongside Value.
    public Il2CppMethodPointer(ulong addr) => Value = addr;

    public override readonly string ToString() => $"0x{Value:X}";

    public static implicit operator ulong(Il2CppMethodPointer ptr) => ptr.Value;

    public static implicit operator Il2CppMethodPointer(ulong ptr) => new(ptr);
}
