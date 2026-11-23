using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.ABC;

internal sealed class AbcNativeModel(TypeModel model) : MoraxNativeModel(model)
{
    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
    {
        var count = (type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition).VTableCount;
        // Class::FromTypeDefinition 0x1807D03A0 and GenericClass::GetClass 0x18079AE50 allocate a 0x128-byte prefix.
        return layouts.Declare(
            ClassName(type),
            0x128 + count * 16,
            node =>
            {
                NativeLayoutAnalysisModel.Add(node, "header", layouts.Cpp.GetType("Il2CppClass_0"), 0);
                if (count > 0)
                    NativeLayoutAnalysisModel.Add(node, "vtable", layouts.Cpp.GetType("VirtualInvokeData").AsArray(count), 0x128);
            }
        );
    }
}
