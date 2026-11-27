# Integration Tests

`Program.cs` dispatches test modes across `Common/`, `CLI/`, `Plugin/` and `VersionedSerialization/`.

```bash
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --serialization
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --game <GameId> '<sample Native directory>' ['<output directory>']
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --cpp-layout [clang]
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --unity-headers [clang]
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --metadata-version
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --plugin-cli '<CLI DLL>' '<isolated directory>'
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --cli-targets '<CLI DLL>'
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --progress
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --pdb-fixture '<output directory>' [llvm-pdbutil] ['<IDA idat executable>']
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --dwarf-fixture '<output directory>' [readelf] [gdb] ['<IDA idat executable>'] [llvm-symbolizer]
dotnet run --project tests/Il2CppInspector.Tests -c Release -- --dwarf-globals '<Android sample directory>' '<output directory>' ['<IDA idat executable>']
python -I tests/Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
```

- `--plugins` verifies case-insensitive selection, truncated/foreign input rejection, runtime header parsing and `Samples` registration for every installed plugin.
- `--serialization` verifies versioned value/reference array construction and field boundaries across versions.
- `--unity-headers` checks every embedded runtime/API header, 32/64-bit split class layouts, branch-specific metadata transitions, compact metadata records and rejection of prerelease Unity versions and unsupported metadata formats. Synthetic PE/metadata pairs exercise version 108 registration scanning, generic method tables, invokers and usage decoding in both pointer widths. Supplying `clang` compiles every type header for x86, ARM, ARM64 and Windows x64 and checks native offsets.
- `--metadata-version` exercises the protected `GamePlugin.DetectMetadataVersions` API through a plugin fixture. It covers 32/64-bit binaries, erased or incorrect version words, same-size 29/31 headers, the 106/107 ambiguity, nil tokens, invalid records/native tables, input preservation, cancellation and isolation from normal auto-detect.
- `--game` checks input matching, concrete value-type header sizes, decoded table/layout baselines (`Plugin/GameSampleTests.cs`), and optional DummyDll reference resolution (stage NSH `common.dat` as `global-metadata.dat`).
- `--cpp-layout`, `--pdb-fixture`, `--dwarf-fixture` and `--dwarf-globals` run standalone checks by default and invoke external validators (`clang`, `lld`, `llvm-objcopy`, `llvm-pdbutil`, `readelf`, `gdb`, `llvm-symbolizer`, `idat`) when supplied.
- `--pdb-fixture` also verifies RSDS identity recovery through PE RVA mapping when unpacking leaves a stale or out-of-file CodeView pointer, preserves valid file-pointer behavior, and rejects invalid signatures, truncated records and unterminated paths.
- `--plugin-cli` verifies flat `plugins/` packaging, `runtimes/<rid>/native/` native layout, `--list-games` / `--list-games --json-progress` output, and missing-plugin error reporting.
- `ScriptResourcesTests.py` tests disassembler script templates, `function_boundaries_only` mode and `_t.py` terminal checkpoint/resume without requiring IDA or `tqdm`. Terminal checkpoints accept relocated scripts/data/databases and edited scripts, including older checkpoint files that stored paths and a script hash.

Only final (`f`) and patch (`p`) Unity releases are supported. Alpha, beta and release-candidate versions are rejected when explicitly selected. The Unity header ranges include build suffixes and stop at the last verified snapshot of each branch. A version without a suffix selects its first final release (`f1`). Metadata 29/31 with the `2022` tag uses the extra unresolved instance/static call tables; it must not be confused with Unity 2021.3, which changes from 29 to 31 at 2021.3.40f1. The corresponding changes occur at 2022.3.33f1 and 6000.0.10f1 on those branches.

Other source boundaries covered by the header tests include `Il2CppTypeDefinition::ccwFunctionIndex` and `Il2CppCodeRegistration::ccwMarshalingFunctions` at 5.3.4p1 (20.1), Windows Runtime factory tables at 2018.4.18f1, and `Il2CppType::valuetype` in 2021.1 (27.2). Unity 6000.3.3f1 / 6000.4.0f1 adds the generic-parameter-flags union; 6000.5 moves class event/property/nested-type caches and adds inline arrays. Compiler checks cover the absolute alignment of `Il2CppClass::cctor_thread` after splitting the class for generated declarations.

The released versions 5.3.1p1–p2 (metadata 18), 6000.5.1f1–6000.5.11f1 (107), and 6000.6.0f1–6000.6.11f1 (108) have explicit type/API ranges. Version 18 uses metadata usages without TypeDef/Image tokens. Version 107 retains the 106 layouts. Version 108 reads three generic-method specification categories, two generic-function tables and compact invoker indices from metadata, normalizes the smaller generic-class context, and uses the revised interface and metadata-usage layouts. The section definitions follow `Il2CppGlobalMetadataHeader`; compact record decoding follows `MetadataDeserialization` and `GlobalMetadata::BuildGenericMethodFromMethodSpec` in Unity 6000.6.0f1.

Prerelease-only metadata formats (including 35, 38, 104 and 105) and the legacy-assembly variant of metadata 27 are rejected. Metadata versions shared with final releases cannot identify the Unity build channel by themselves; automatic header selection only offers final/patch ranges. Source/header and synthetic-binary validation do not replace executable-sample validation for each Unity release.

Plugins with a normalized metadata file whose version word is missing can explicitly call `DetectMetadataVersions(binary, metadata, options)`. The metadata signature, section descriptors and records must remain intact. The API returns all final/patch header versions passing metadata and binary validation, preserves the borrowed stream position and input bytes, and never picks an arbitrary candidate. Empty or ambiguous results require plugin handling. It does not run during ordinary plugin selection or stock metadata loading.

`Cy_JP_Windows_x.x.x` restores the embedded metadata signature after emulation, probes its header version against the recovered binary and accepts only one candidate. It no longer assumes metadata 31, a 256-byte header or 36-byte method records.
