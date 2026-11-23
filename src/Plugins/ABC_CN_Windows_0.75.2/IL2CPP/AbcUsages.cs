using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Plugins;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata
{
    private readonly List<AdditionalMetadataUsage> _additionalUsages = [];
    public override IEnumerable<AdditionalMetadataUsage> AdditionalUsages => _additionalUsages;

    private void ReadUsages()
    {
        // InitializeRuntimeMetadata 0x1807D0F10: four-bit kinds and a shared cache indexed by the low 24 bits.
        const int count = (0x49B9F0 - 0x436F48) / 4;
        var fieldRefs = 0;
        for (var i = 0; i < count; i++)
        {
            var value = U32(_data, 0x436F48 + i * 4);
            var kind = value >> 28;
            var index = (int)(value & 0x0FFFFFFF);
            var address = Image.ImageBase + 0x223C5B0 + (ulong)i * 8;
            if (kind is >= 1 and <= 6)
            {
                Usages.Add(new((MetadataUsageType)kind, index, address));
                if (kind == 4)
                    fieldRefs = Math.Max(fieldRefs, index + 1);
            }
            else if (kind is 7 or 8)
                _additionalUsages.Add(new("moraxRuntimeCaches", -1, address, $"RuntimeCache_{i}", "void *"));
            else if (kind != 0)
                throw new InvalidDataException($"ABC metadata usage {i} has unsupported kind {kind}.");
        }
        Metadata.FieldRefs = Records(
            _data,
            0xCDC664,
            fieldRefs,
            8,
            static (d, _) => new Il2CppFieldRef { FieldIndex = unchecked(I32(d, 0) - 0x24D378DE), TypeIndex = unchecked(I32(d, 4) - 0x4E1DBC5C) }
        );
        // GetStringLiteralFromIndex 0x1807F9D80 uses a separate linked BSS cache, outside the shared usage table.
        for (var i = 0; i < Metadata.StringLiterals.Length; i++)
            Usages.Add(new(MetadataUsageType.StringLiteral, i, Image.ImageBase + 0x2305B00 + (ulong)i * 8));
    }
}
