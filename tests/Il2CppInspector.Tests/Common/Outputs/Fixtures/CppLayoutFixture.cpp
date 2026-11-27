#include <stddef.h>
#include <stdint.h>
#if __SIZEOF_POINTER__ == 8
#define IS_64BIT
#else
#define IS_32BIT
#endif
#include "Cpp/UnityHeaders/39-6000.3.0f1-6000.3.2f1.h"

struct Tail { void* pointer; uint8_t byte; };
struct Nested { uint8_t byte; Tail value; uint32_t after; };
static_assert(sizeof(Tail) == sizeof(void*) * 2);
static_assert(offsetof(Nested, value) == sizeof(void*));
static_assert(sizeof(Nested) == sizeof(void*) * 4);
static_assert(sizeof(MethodInfo) == (sizeof(void*) == 8 ? 88 : 48));
static_assert(sizeof(Il2CppTypeEnum) == 4);
static_assert(sizeof(((Il2CppClass*)nullptr)->initializationExceptionGCHandle) == sizeof(void*));
#if __SIZEOF_POINTER__ == 8
static_assert(offsetof(Il2CppClass, stack_slot_size) == 0xFC);
static_assert(offsetof(Il2CppClass, vtable_count) == 0x12A);
static_assert(offsetof(Il2CppClass, packingSize) == 0x134);
static_assert(offsetof(Il2CppClass, vtable) == 0x138);
static_assert(offsetof(Il2CppClass_Merged, vtable) == 0x138);
static_assert(sizeof(Il2CppCodeRegistration) == 0x88);
static_assert(offsetof(Il2CppCodeRegistration, interopDataCount) == 0x58);
static_assert(offsetof(Il2CppCodeRegistration, codeGenModules) == 0x80);
#endif
