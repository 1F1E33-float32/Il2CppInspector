using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Plugin.Morax;

namespace Il2CppInspector.Plugin.ABC;

internal static class AbcRuntimeHeaders
{
    // Class::FromTypeDefinition 0x1807D03A0 and SetupFields/Methods 0x18078D150 / 0x18078D490.
    private const string ClassBody = """
            const Il2CppImage* image;
            void* gc_desc;
            const void* typeMetadataHandle;
            Il2CppClass** implementedInterfaces;
            Il2CppType this_arg;
            Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets;
            Il2CppClass* declaringType;
            const MethodInfo** methods;
            Il2CppClass* element_class;
            const Il2CppInteropData* interopData;
            uint8_t reserved58[16];
            Il2CppClass** typeHierarchy;
            Il2CppGenericClass* generic_class;
            uint64_t namespaceIdentifier;
            Il2CppClass* parent;
            Il2CppClass* klass;
            EventInfo* events;
            Il2CppClass* castClass;
            Il2CppType byval_arg;
            uint64_t nameIdentifier;
            const Il2CppRGCTXData* rgctx_data;
            const PropertyInfo* properties;
            FieldInfo* fields;
            void* static_fields;
            uint8_t initialization[24];
            uint32_t flags;
            uint8_t reservedF4[4];
            const void* genericContainer;
            uint32_t thread_static_fields_offset;
            uint16_t event_count;
            uint16_t instance_size;
            uint16_t method_count;
            uint16_t vtable_count;
            uint16_t property_count;
            uint16_t nested_type_count;
            uint16_t actualSize;
            uint16_t reserved112;
            uint16_t interfaces_count;
            uint16_t static_fields_size;
            uint16_t native_size;
            uint16_t thread_static_fields_size;
            uint16_t interface_offsets_count;
            uint16_t field_count;
            uint8_t packingSize;
            uint8_t recursionDepth;
            uint8_t typeHierarchyDepth;
            uint8_t minimumAlignment;
            uint8_t rank;
            uint8_t flags125;
            uint8_t flags126;
            uint8_t reserved127;
        """;

    // Registrations are compiled out; opaque records contain encoded members
    // (0x18079E950, 0x1807D2120, 0x18078EBF0 and 0x18078EF20), so stock declarations cannot describe them.
    private static readonly (string Name, string Body)[] Records =
    [
        ("Il2CppClass_0", ClassBody),
        ("Il2CppClass_1", ""),
        ("Il2CppClass", ClassBody + "\n    VirtualInvokeData vtable[1];"),
        // MethodInfo is 0x40 bytes; names and signature types live behind its lazy detail pointer.
        (
            "MethodInfo",
            """
                InvokerMethod invoker_method;
                const void* genericHandle;
                Il2CppMethodPointer methodPointer;
                Il2CppMethodPointer virtualMethodPointer;
                Il2CppClass* klass;
                const void* methodMetadataHandle;
                uint16_t slot;
                uint16_t flags;
                uint16_t iflags;
                uint8_t parameters_count;
                uint8_t bitflags;
                void* details;
            """
        ),
        // FieldInfo pointers and offsets carry arithmetic/XOR masks (0x18078D150).
        (
            "FieldInfo",
            """
                uint64_t nameIdentifier;
                uint64_t encodedParent;
                uint64_t encodedType;
                uint32_t encodedOffset;
                uint32_t reserved;
            """
        ),
        (
            "Il2CppType",
            """
                union
                {
                    void* dummy;
                    const void* metadataHandle;
                    const Il2CppType* type;
                    Il2CppArrayType* array;
                    Il2CppGenericClass* generic_class;
                } data;
                uint16_t attrs;
                uint8_t type;
                uint8_t modifiers;
                uint8_t enumType;
                uint8_t reserved[3];
            """
        ),
        ("Il2CppCodeRegistration", null),
        ("Il2CppMetadataRegistration", null),
        ("Il2CppImage", null),
        ("Il2CppAssembly", null),
        ("Il2CppAssemblyName", null),
        ("PropertyInfo", null),
        ("EventInfo", null),
    ];

    internal static UnityHeaders GetHeaders() =>
        UnityHeaders.ForPlugin(new("2021.2.0"), new("2022.3.14f1"), static text => MoraxPlugin.ApplyRuntimeRecords(text, Records));
}
