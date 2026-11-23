using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Il2CppInspector.Plugin.ABC;

internal static class AbcNames
{
    // String identifier hashing is 0x1807D32D0; Runtime::Init (0x1807D48E0) requests these CLR names.
    // Only exact hash matches are recovered. Other identifiers retain the runtime's hexadecimal spelling.
    private static readonly Dictionary<ulong, string> KnownNames = new[]
    {
        "System",
        "System.Runtime.InteropServices",
        "System.Runtime.CompilerServices",
        "Void",
        "Boolean",
        "Char",
        "SByte",
        "Byte",
        "Int16",
        "UInt16",
        "Int32",
        "UInt32",
        "Int64",
        "UInt64",
        "IntPtr",
        "UIntPtr",
        "Single",
        "Double",
        "Decimal",
        "String",
        "Object",
        "Array",
        "Enum",
        "ValueType",
        "Type",
        "Attribute",
        "SerializableAttribute",
        "ComVisibleAttribute",
        "MulticastDelegate",
        "Delegate",
        "Nullable`1",
        "TypedReference",
        "RuntimeArgumentHandle",
        "RuntimeFieldHandle",
        "RuntimeMethodHandle",
        "RuntimeTypeHandle",
        "SByteEnum",
        "ByteEnum",
        "Int16Enum",
        "UInt16Enum",
        "Int32Enum",
        "UInt32Enum",
        "Int64Enum",
        "UInt64Enum",
    }.ToDictionary(static n => Hash(Encoding.UTF8.GetBytes(n)), static n => n);

    internal static string DecodeIdentifier(ulong identifier) => KnownNames.GetValueOrDefault(identifier, identifier.ToString("X16"));

    private static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        const ulong prime = 0x9FB21C651E98DF25;
        var n = bytes.Length;
        ulong value;
        unchecked
        {
            // Every candidate above is 4..32 UTF-8 bytes; the other runtime hash branches are unused.
            if (n <= 8)
            {
                value =
                    ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(bytes) << 32 | BinaryPrimitives.ReadUInt32LittleEndian(bytes[(n - 4)..]))
                    ^ 0xE7FD3E9CE2529BAE;
                value = prime * (value ^ BitOperations.RotateLeft(value, 49) ^ BitOperations.RotateLeft(value, 24));
                value = prime * (value ^ ((ulong)n + (value >> 35)));
                value ^= value >> 28;
            }
            else
            {
                if (n <= 16)
                {
                    var low = BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^ 0x92875BB0CDDC73AD;
                    var high = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(n - 8)..]) ^ 0x8451D40625FC1846;
                    value = BinaryPrimitives.ReverseEndianness(low) + high + (ulong)n + Fold(low, high);
                }
                else
                {
                    value = 0x9E3779B185EBCA87 * (ulong)n;
                    value += Mix(bytes, 0, 0xE9508C581D0738AC, 0xF1A839C248E6C788) + Mix(bytes, n - 16, 0x069C78B8CD080ED2, 0xF462CB82C10A067E);
                }
                value = (value ^ value >> 37) * 0x165667919E3779F9;
                value ^= value >> 32;
            }
            return value | 0xA000000000000000;
        }
    }

    private static ulong Mix(ReadOnlySpan<byte> bytes, int offset, ulong low, ulong high) =>
        Fold(BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]) ^ low, BinaryPrimitives.ReadUInt64LittleEndian(bytes[(offset + 8)..]) ^ high);

    private static ulong Fold(ulong low, ulong high)
    {
        var product = (UInt128)low * high;
        return (ulong)product ^ (ulong)(product >> 64);
    }
}
