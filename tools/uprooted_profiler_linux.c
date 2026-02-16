#include <dlfcn.h>
#include <unistd.h>
#include <sys/time.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <stdlib.h>
#include <stdarg.h>
#include <limits.h>
#include <time.h>

typedef uint16_t WCHAR;
typedef const WCHAR* LPCWSTR;
typedef uint8_t  BYTE;
typedef uint16_t USHORT;
typedef uint32_t ULONG;
typedef uint32_t DWORD;
typedef int32_t  HRESULT;
typedef int      BOOL;
typedef uintptr_t UINT_PTR;

#ifndef TRUE
#define TRUE 1
#endif
#ifndef FALSE
#define FALSE 0
#endif

static const WCHAR W_UprootedHook_Entry[] = {
    'U','p','r','o','o','t','e','d','H','o','o','k','.','E','n','t','r','y', 0
};
static const WCHAR W_System_Object[] = {
    'S','y','s','t','e','m','.','O','b','j','e','c','t', 0
};
static const WCHAR W_System_Reflection_Assembly[] = {
    'S','y','s','t','e','m','.','R','e','f','l','e','c','t','i','o','n','.','A','s','s','e','m','b','l','y', 0
};
static const WCHAR W_System_Exception[] = {
    'S','y','s','t','e','m','.','E','x','c','e','p','t','i','o','n', 0
};
static const WCHAR W_LoadFrom[] = {
    'L','o','a','d','F','r','o','m', 0
};
static const WCHAR W_CreateInstance[] = {
    'C','r','e','a','t','e','I','n','s','t','a','n','c','e', 0
};
static const WCHAR W_System_Private_CoreLib[] = {
    'S','y','s','t','e','m','.','P','r','i','v','a','t','e','.','C','o','r','e','L','i','b', 0
};
static const WCHAR W_System_Dot[] = {
    'S','y','s','t','e','m','.', 0
};
static const WCHAR W_Microsoft_Dot[] = {
    'M','i','c','r','o','s','o','f','t','.', 0
};

static size_t u16len(const WCHAR* s) {
    size_t len = 0;
    while (s[len]) len++;
    return len;
}

static int u16cmp(const WCHAR* a, const WCHAR* b) {
    while (*a && *b && *a == *b) { a++; b++; }
    return (int)*a - (int)*b;
}

static int u16ncmp(const WCHAR* a, const WCHAR* b, size_t n) {
    for (size_t i = 0; i < n; i++) {
        if (a[i] != b[i]) return (int)a[i] - (int)b[i];
        if (!a[i]) return 0;
    }
    return 0;
}

static int u16_to_utf8(const WCHAR* src, char* dst, size_t dst_size) {
    int pos = 0;
    while (*src && (size_t)pos < dst_size - 1) {
        uint16_t c = *src++;
        if (c < 0x80) {
            dst[pos++] = (char)c;
        } else if (c < 0x800) {
            if ((size_t)pos + 2 > dst_size - 1) break;
            dst[pos++] = (char)(0xC0 | (c >> 6));
            dst[pos++] = (char)(0x80 | (c & 0x3F));
        } else {

            if (c >= 0xD800 && c <= 0xDBFF && *src >= 0xDC00 && *src <= 0xDFFF) {
                uint32_t cp = 0x10000 + ((uint32_t)(c - 0xD800) << 10) + (*src++ - 0xDC00);
                if ((size_t)pos + 4 > dst_size - 1) break;
                dst[pos++] = (char)(0xF0 | (cp >> 18));
                dst[pos++] = (char)(0x80 | ((cp >> 12) & 0x3F));
                dst[pos++] = (char)(0x80 | ((cp >> 6) & 0x3F));
                dst[pos++] = (char)(0x80 | (cp & 0x3F));
            } else {
                if ((size_t)pos + 3 > dst_size - 1) break;
                dst[pos++] = (char)(0xE0 | (c >> 12));
                dst[pos++] = (char)(0x80 | ((c >> 6) & 0x3F));
                dst[pos++] = (char)(0x80 | (c & 0x3F));
            }
        }
    }
    dst[pos] = '\0';
    return pos;
}

static int utf8_to_u16(const char* src, WCHAR* dst, size_t dst_chars) {
    int pos = 0;
    const unsigned char* s = (const unsigned char*)src;
    while (*s && (size_t)pos < dst_chars - 1) {
        if (*s < 0x80) {
            dst[pos++] = *s++;
        } else if ((*s & 0xE0) == 0xC0) {
            uint32_t cp = (*s++ & 0x1F) << 6;
            if (*s) cp |= (*s++ & 0x3F);
            dst[pos++] = (WCHAR)cp;
        } else if ((*s & 0xF0) == 0xE0) {
            uint32_t cp = (*s++ & 0x0F) << 12;
            if (*s) { cp |= (*s++ & 0x3F) << 6; }
            if (*s) { cp |= (*s++ & 0x3F); }
            dst[pos++] = (WCHAR)cp;
        } else if ((*s & 0xF8) == 0xF0) {
            uint32_t cp = (*s++ & 0x07) << 18;
            if (*s) { cp |= (*s++ & 0x3F) << 12; }
            if (*s) { cp |= (*s++ & 0x3F) << 6; }
            if (*s) { cp |= (*s++ & 0x3F); }

            if (cp >= 0x10000 && (size_t)pos + 2 <= dst_chars - 1) {
                cp -= 0x10000;
                dst[pos++] = (WCHAR)(0xD800 + (cp >> 10));
                dst[pos++] = (WCHAR)(0xDC00 + (cp & 0x3FF));
            } else {
                dst[pos++] = (WCHAR)'?';
            }
        } else {
            s++;
        }
    }
    dst[pos] = 0;
    return pos;
}

static const WCHAR* u16str(const WCHAR* haystack, const WCHAR* needle) {
    if (!*needle) return haystack;
    size_t nlen = u16len(needle);
    for (; *haystack; haystack++) {
        if (u16ncmp(haystack, needle, nlen) == 0)
            return haystack;
    }
    return NULL;
}

static char g_hookDllPath[PATH_MAX];
static WCHAR g_hookDllPathW[PATH_MAX];
static char g_logFilePath[PATH_MAX];

static void InitPaths(void) {
    const char* home = getenv("HOME");
    if (!home) home = "/tmp";
    snprintf(g_hookDllPath, PATH_MAX, "%s/.local/share/uprooted/UprootedHook.dll", home);
    snprintf(g_logFilePath, PATH_MAX, "%s/.local/share/uprooted/profiler.log", home);
    utf8_to_u16(g_hookDllPath, g_hookDllPathW, PATH_MAX);
}

typedef struct {
    unsigned long  Data1;
    unsigned short Data2;
    unsigned short Data3;
    unsigned char  Data4[8];
} MYGUID;

static int MyIsEqualGUID(const MYGUID* a, const MYGUID* b) {
    return memcmp(a, b, sizeof(MYGUID)) == 0;
}

static const MYGUID CLSID_UprootedProfiler =
    { 0xD1A6F5A0, 0x1234, 0x4567, { 0x89, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67 } };
static const MYGUID MY_IID_IUnknown =
    { 0x00000000, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 } };
static const MYGUID MY_IID_IClassFactory =
    { 0x00000001, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 } };

static const MYGUID IID_ICorProfilerCallback =
    { 0x176FBED1, 0xA55C, 0x4796, { 0x98, 0xCA, 0xA9, 0xDA, 0x0E, 0xF8, 0x83, 0xE7 } };
static const MYGUID IID_ICorProfilerCallback2 =
    { 0x8A8CC829, 0xCCF2, 0x49FE, { 0xBB, 0xAE, 0x0F, 0x02, 0x22, 0x28, 0x07, 0x1A } };
static const MYGUID IID_ICorProfilerCallback3 =
    { 0x4FD2ED52, 0x7731, 0x4B8D, { 0x94, 0x69, 0x03, 0xD2, 0xCC, 0x30, 0x86, 0xC5 } };
static const MYGUID IID_ICorProfilerCallback4 =
    { 0x7B63B2E3, 0x107D, 0x4D48, { 0xB2, 0xF6, 0xF6, 0x1E, 0x22, 0x94, 0x70, 0xD2 } };
static const MYGUID IID_ICorProfilerCallback5 =
    { 0x8DFBA405, 0x8C9F, 0x45F8, { 0xBF, 0xFA, 0x83, 0xB1, 0x4C, 0xEF, 0x78, 0xB5 } };
static const MYGUID IID_ICorProfilerCallback6 =
    { 0xFC13DF4B, 0x4448, 0x4F4F, { 0x95, 0x0C, 0xBA, 0x8D, 0x19, 0xD0, 0x0C, 0x36 } };
static const MYGUID IID_ICorProfilerCallback7 =
    { 0xF76A2DBA, 0x1D52, 0x4539, { 0x86, 0x6C, 0x2A, 0xA5, 0x18, 0xF9, 0xEF, 0xC3 } };
static const MYGUID IID_ICorProfilerCallback8 =
    { 0x5BED9B15, 0xC079, 0x4D47, { 0xBF, 0xE2, 0x21, 0x5A, 0x14, 0x0C, 0x07, 0xE0 } };
static const MYGUID IID_ICorProfilerCallback9 =
    { 0x27583EC3, 0xC8F5, 0x482F, { 0x80, 0x52, 0x19, 0x4B, 0x8C, 0xE4, 0x70, 0x5A } };
static const MYGUID IID_ICorProfilerCallback10 =
    { 0xCEC5B60E, 0xC69C, 0x495F, { 0x87, 0xF6, 0x84, 0xD2, 0x8E, 0xE1, 0x6F, 0xFB } };
static const MYGUID IID_ICorProfilerCallback11 =
    { 0x42350846, 0xAAED, 0x47F7, { 0xB1, 0x28, 0xFD, 0x0C, 0x98, 0x88, 0x1C, 0xDE } };

static const MYGUID IID_ICorProfilerInfo =
    { 0x28B5557D, 0x3F3F, 0x48B4, { 0x90, 0xB2, 0x5F, 0x9E, 0xEA, 0x2F, 0x6C, 0x48 } };
static const MYGUID IID_IMetaDataImport =
    { 0x7DAC8207, 0xD3AE, 0x4c75, { 0x9B, 0x67, 0x92, 0x80, 0x1A, 0x49, 0x7D, 0x44 } };
static const MYGUID IID_IMetaDataEmit =
    { 0xBA3FEE4C, 0xECB9, 0x4e41, { 0x83, 0xB7, 0x18, 0x3F, 0xA4, 0x1C, 0xD8, 0x59 } };

#define VT_PI_GetFunctionInfo           15
#define VT_PI_SetEventMask              16
#define VT_PI_GetModuleInfo             20
#define VT_PI_GetModuleMetaData         21
#define VT_PI_GetILFunctionBody         22
#define VT_PI_GetILFunctionBodyAllocator 23
#define VT_PI_SetILFunctionBody         24

#define VT_MI_CloseEnum       3
#define VT_MI_EnumTypeDefs    6
#define VT_MI_EnumTypeRefs    8
#define VT_MI_FindTypeDefByName   9
#define VT_MI_GetTypeRefProps 14
#define VT_MI_EnumMethods    18
#define VT_MI_FindMethod         27
#define VT_MI_GetMethodProps     30
#define VT_MI_FindTypeRef    55

#define VT_ME_DefineTypeRefByName 12
#define VT_ME_DefineMemberRef    14
#define VT_ME_DefineUserString   28

#define COR_PRF_MONITOR_MODULE_LOADS    0x00000004
#define COR_PRF_MONITOR_JIT_COMPILATION 0x00000020

#define ofRead   0x00000000
#define ofWrite  0x00000001

#define IL_NOP       0x00
#define IL_LDSTR     0x72
#define IL_CALL      0x28
#define IL_CALLVIRT  0x6F
#define IL_POP       0x26
#define IL_LEAVE_S   0xDE
#define IL_RET       0x2A

#define CorILMethod_TinyFormat  0x02
#define CorILMethod_FatFormat   0x03
#define CorILMethod_MoreSects   0x08
#define CorILMethod_InitLocals  0x10

#define CorILMethod_Sect_EHTable   0x01
#define CorILMethod_Sect_FatFormat 0x40

static volatile int32_t g_pathsInitialized = 0;

static void EnsurePathsInitialized(void) {
    if (__sync_val_compare_and_swap(&g_pathsInitialized, 0, 1) == 0) {
        InitPaths();
    }
}

static FILE* g_logFile = NULL;

static void PLog(const char* msg) {
    EnsurePathsInitialized();
    if (!g_logFile) {
        g_logFile = fopen(g_logFilePath, "a");
    }
    if (g_logFile) {
        struct timeval tv;
        gettimeofday(&tv, NULL);
        struct tm* lt = localtime(&tv.tv_sec);
        fprintf(g_logFile, "[%02d:%02d:%02d.%03d] %s\n",
                lt->tm_hour, lt->tm_min, lt->tm_sec,
                (int)(tv.tv_usec / 1000), msg);
        fflush(g_logFile);
    }
}

static void PLogFmt(const char* fmt, ...) {
    char buf[2048];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    PLog(buf);
}

static void LogGUID(const char* label, const MYGUID* g) {
    PLogFmt("%s {%08lX-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X}",
            label, (unsigned long)g->Data1, g->Data2, g->Data3,
            g->Data4[0], g->Data4[1], g->Data4[2], g->Data4[3],
            g->Data4[4], g->Data4[5], g->Data4[6], g->Data4[7]);
}

typedef struct UprootedProfiler UprootedProfiler;
static BOOL DoInjectIL(UINT_PTR moduleId, unsigned int methodToken);

static volatile int32_t g_refCount = 1;
static void* g_profilerInfo = NULL;
static volatile int32_t g_injectionCount = 0;
static volatile int32_t g_jitCount = 0;
static volatile int32_t g_moduleCount = 0;
static UINT_PTR g_corelibModuleId = 0;

static UINT_PTR g_targetModuleId = 0;

static unsigned int g_tokLoadFromMR = 0;
static unsigned int g_tokCreateInstMR = 0;
static unsigned int g_tokExceptionTR = 0;
static unsigned int g_tokPathString = 0;
static unsigned int g_tokTypeString = 0;

static volatile int32_t g_targetReady = 0;

static void** GetInfoVtable(void) {
    return *(void***)g_profilerInfo;
}

static HRESULT Stub_OK(void) { return 0; }

static int IsProfilerCallbackGUID(const MYGUID* riid) {
    return MyIsEqualGUID(riid, &IID_ICorProfilerCallback) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback2) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback3) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback4) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback5) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback6) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback7) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback8) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback9) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback10) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback11);
}

static HRESULT Prof_QueryInterface(UprootedProfiler* self,
                                   const MYGUID* riid, void** ppv) {
    if (!ppv) return 0x80004003;
    if (MyIsEqualGUID(riid, &MY_IID_IUnknown) || IsProfilerCallbackGUID(riid)) {
        *ppv = self;
        __sync_add_and_fetch(&g_refCount, 1);
        return 0;
    }
    LogGUID("QI: REJECTED", riid);
    *ppv = NULL;
    return 0x80004002;
}

static ULONG Prof_AddRef(UprootedProfiler* self) {
    return __sync_add_and_fetch(&g_refCount, 1);
}

static ULONG Prof_Release(UprootedProfiler* self) {
    return __sync_sub_and_fetch(&g_refCount, 1);
}

static int CompressToken(unsigned int token, BYTE* buf) {
    unsigned int table = (token >> 24);
    unsigned int rid = token & 0x00FFFFFF;
    unsigned int tag;
    unsigned int coded;

    if (table == 0x02) tag = 0;
    else if (table == 0x01) tag = 1;
    else tag = 2;

    coded = (rid << 2) | tag;

    if (coded < 0x80) {
        buf[0] = (BYTE)coded;
        return 1;
    } else if (coded < 0x4000) {
        buf[0] = (BYTE)(0x80 | (coded >> 8));
        buf[1] = (BYTE)(coded & 0xFF);
        return 2;
    } else {
        buf[0] = (BYTE)(0xC0 | ((coded >> 24) & 0x1F));
        buf[1] = (BYTE)((coded >> 16) & 0xFF);
        buf[2] = (BYTE)((coded >> 8) & 0xFF);
        buf[3] = (BYTE)(coded & 0xFF);
        return 4;
    }
}

static void SafeRelease(void* pInterface) {
    if (pInterface) {
        typedef ULONG (*ReleaseFn)(void*);
        ((ReleaseFn)(*(void***)pInterface)[2])(pInterface);
    }
}

typedef HRESULT (*GetModuleMetaDataFn)(
    void* self, UINT_PTR moduleId, DWORD openFlags,
    const MYGUID* riid, void** ppOut);

typedef void (*CloseEnumFn)(void* self, void* hEnum);
typedef HRESULT (*EnumTypeRefsFn)(
    void* self, void** phEnum, unsigned int rTypeRefs[], ULONG cMax, ULONG* pcTypeRefs);
typedef HRESULT (*GetTypeRefPropsFn)(
    void* self, unsigned int tr, unsigned int* ptkResolutionScope,
    WCHAR* szName, ULONG cchName, ULONG* pchName);

static unsigned int SearchTypeRef(void* pImport, void** importVt,
                                   const WCHAR* targetName, unsigned int* pScope) {
    CloseEnumFn closeEnum = (CloseEnumFn)importVt[VT_MI_CloseEnum];
    EnumTypeRefsFn enumTypeRefs = (EnumTypeRefsFn)importVt[VT_MI_EnumTypeRefs];
    GetTypeRefPropsFn getTypeRefProps = (GetTypeRefPropsFn)importVt[VT_MI_GetTypeRefProps];

    void* hEnum = NULL;
    unsigned int typeRefs[64];
    ULONG count = 0;
    unsigned int result = 0;

    HRESULT hr = enumTypeRefs(pImport, &hEnum, typeRefs, 64, &count);
    while (hr == 0 && count > 0) {
        for (ULONG i = 0; i < count; i++) {
            WCHAR trName[512];
            memset(trName, 0, sizeof(trName));
            ULONG trNameLen = 0;
            unsigned int trScope = 0;
            getTypeRefProps(pImport, typeRefs[i], &trScope, trName, 512, &trNameLen);
            if (u16cmp(trName, targetName) == 0) {
                result = typeRefs[i];
                if (pScope) *pScope = trScope;
                break;
            }
        }
        if (result) break;
        hr = enumTypeRefs(pImport, &hEnum, typeRefs, 64, &count);
    }
    if (hEnum) closeEnum(pImport, hEnum);
    return result;
}

static void LogTypeRefCount(void* pImport, void** importVt) {
    EnumTypeRefsFn enumTypeRefs = (EnumTypeRefsFn)importVt[VT_MI_EnumTypeRefs];
    CloseEnumFn closeEnum = (CloseEnumFn)importVt[VT_MI_CloseEnum];
    GetTypeRefPropsFn getTypeRefProps = (GetTypeRefPropsFn)importVt[VT_MI_GetTypeRefProps];

    void* hEnum = NULL;
    unsigned int typeRefs[256];
    ULONG count = 0;
    ULONG total = 0;

    HRESULT hr = enumTypeRefs(pImport, &hEnum, typeRefs, 256, &count);
    if (hr == 0) {
        total = count;

        for (ULONG i = 0; i < count && i < 5; i++) {
            WCHAR trName[256];
            memset(trName, 0, sizeof(trName));
            ULONG trNameLen = 0;
            unsigned int trScope = 0;
            getTypeRefProps(pImport, typeRefs[i], &trScope, trName, 256, &trNameLen);
            char narrow[512];
            u16_to_utf8(trName, narrow, sizeof(narrow));
            PLogFmt("    TypeRef[%lu]: 0x%08X scope=0x%08X %s",
                    (unsigned long)i, typeRefs[i], trScope, narrow);
        }

        while (1) {
            hr = enumTypeRefs(pImport, &hEnum, typeRefs, 256, &count);
            if (hr != 0 || count == 0) break;
            total += count;
        }
    }
    if (hEnum) closeEnum(pImport, hEnum);
    PLogFmt("  Total TypeRefs: %lu", (unsigned long)total);
}

static BOOL PrepareTargetModule(UINT_PTR moduleId) {
    void** vt = GetInfoVtable();
    HRESULT hr;

    GetModuleMetaDataFn getMetaData = (GetModuleMetaDataFn)vt[VT_PI_GetModuleMetaData];


    void* pImport = NULL;
    hr = getMetaData(g_profilerInfo, moduleId, ofRead, &IID_IMetaDataImport, &pImport);
    if (hr != 0 || !pImport) {
        PLogFmt("  IMetaDataImport failed hr=0x%08X", hr);
        return FALSE;
    }

    void** importVt = *(void***)pImport;


    LogTypeRefCount(pImport, importVt);


    unsigned int runtimeScope = 0;
    unsigned int tokObjectTR = SearchTypeRef(pImport, importVt, W_System_Object, &runtimeScope);
    if (!tokObjectTR) {
        PLog("  No System.Object TypeRef, skipping");
        SafeRelease(pImport);
        return FALSE;
    }
    PLogFmt("  System.Object TypeRef=0x%08X scope=0x%08X", tokObjectTR, runtimeScope);


    void* pEmit = NULL;
    hr = getMetaData(g_profilerInfo, moduleId, ofRead | ofWrite, &IID_IMetaDataEmit, &pEmit);
    if (hr != 0 || !pEmit) {
        PLogFmt("  IMetaDataEmit failed hr=0x%08X", hr);
        SafeRelease(pImport);
        return FALSE;
    }
    void** emitVt = *(void***)pEmit;


    unsigned int tokAssemblyTR = SearchTypeRef(pImport, importVt, W_System_Reflection_Assembly, NULL);
    if (tokAssemblyTR) {
        PLogFmt("  Found Assembly TypeRef=0x%08X", tokAssemblyTR);
    } else {
        typedef HRESULT (*DefineTypeRefByNameFn)(
            void* self, unsigned int tkResolutionScope, LPCWSTR szName,
            unsigned int* ptr);
        DefineTypeRefByNameFn defineTypeRef = (DefineTypeRefByNameFn)emitVt[VT_ME_DefineTypeRefByName];

        hr = defineTypeRef(pEmit, runtimeScope, W_System_Reflection_Assembly, &tokAssemblyTR);
        PLogFmt("  DefineTypeRef Assembly hr=0x%08X token=0x%08X", hr, tokAssemblyTR);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (*DefineMemberRefFn)(
            void* self, unsigned int tkImport, LPCWSTR szName,
            const BYTE* pvSigBlob, ULONG cbSigBlob,
            unsigned int* pmr);
        DefineMemberRefFn defineMemberRef = (DefineMemberRefFn)emitVt[VT_ME_DefineMemberRef];


        BYTE sig[16];
        int len = 0;
        sig[len++] = 0x00;
        sig[len++] = 0x01;
        sig[len++] = 0x12;
        len += CompressToken(tokAssemblyTR, sig + len);
        sig[len++] = 0x0E;

        hr = defineMemberRef(pEmit, tokAssemblyTR, W_LoadFrom,
                             sig, (ULONG)len, &g_tokLoadFromMR);
        PLogFmt("  LoadFrom MemberRef hr=0x%08X token=0x%08X (sigLen=%d)",
                hr, g_tokLoadFromMR, len);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (*DefineMemberRefFn)(
            void* self, unsigned int tkImport, LPCWSTR szName,
            const BYTE* pvSigBlob, ULONG cbSigBlob,
            unsigned int* pmr);
        DefineMemberRefFn defineMemberRef = (DefineMemberRefFn)emitVt[VT_ME_DefineMemberRef];


        BYTE sig[] = { 0x20, 0x01, 0x1C, 0x0E };
        hr = defineMemberRef(pEmit, tokAssemblyTR, W_CreateInstance,
                             sig, sizeof(sig), &g_tokCreateInstMR);
        PLogFmt("  CreateInstance MemberRef hr=0x%08X token=0x%08X",
                hr, g_tokCreateInstMR);
        if (hr != 0) goto fail;
    }


    g_tokExceptionTR = SearchTypeRef(pImport, importVt, W_System_Exception, NULL);
    if (g_tokExceptionTR) {
        PLogFmt("  Found Exception TypeRef=0x%08X", g_tokExceptionTR);
    } else {
        typedef HRESULT (*DefineTypeRefByNameFn)(
            void* self, unsigned int tkResolutionScope, LPCWSTR szName,
            unsigned int* ptr);
        DefineTypeRefByNameFn defineTypeRef = (DefineTypeRefByNameFn)emitVt[VT_ME_DefineTypeRefByName];
        hr = defineTypeRef(pEmit, runtimeScope, W_System_Exception, &g_tokExceptionTR);
        PLogFmt("  DefineTypeRef Exception hr=0x%08X token=0x%08X", hr, g_tokExceptionTR);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (*DefineUserStringFn)(
            void* self, LPCWSTR szString, ULONG cchString,
            unsigned int* pstk);
        DefineUserStringFn defineStr = (DefineUserStringFn)emitVt[VT_ME_DefineUserString];

        hr = defineStr(pEmit, g_hookDllPathW, (ULONG)u16len(g_hookDllPathW), &g_tokPathString);
        PLogFmt("  PathString hr=0x%08X token=0x%08X", hr, g_tokPathString);
        if (hr != 0) goto fail;

        hr = defineStr(pEmit, W_UprootedHook_Entry, (ULONG)u16len(W_UprootedHook_Entry), &g_tokTypeString);
        PLogFmt("  TypeString hr=0x%08X token=0x%08X", hr, g_tokTypeString);
        if (hr != 0) goto fail;
    }

    g_targetModuleId = moduleId;
    PLog("  ALL tokens created successfully!");


    {
        typedef HRESULT (*EnumTypeDefsFn)(
            void* self, void** phEnum, unsigned int rTypeDefs[], ULONG cMax, ULONG* pcTypeDefs);
        typedef HRESULT (*EnumMethodsFn)(
            void* self, void** phEnum, unsigned int cl, unsigned int rMethods[], ULONG cMax, ULONG* pcTokens);
        typedef HRESULT (*GetMethodPropsFn)(
            void* self, unsigned int mb, unsigned int* pClass, WCHAR* szMethod,
            ULONG cchMethod, ULONG* pchMethod, DWORD* pdwAttr,
            void** ppvSigBlob, ULONG* pcbSigBlob, ULONG* pulCodeRVA, DWORD* pdwImplFlags);

        CloseEnumFn closeEnum = (CloseEnumFn)importVt[VT_MI_CloseEnum];
        EnumTypeDefsFn enumTypeDefs = (EnumTypeDefsFn)importVt[VT_MI_EnumTypeDefs];
        EnumMethodsFn enumMethods = (EnumMethodsFn)importVt[VT_MI_EnumMethods];
        GetMethodPropsFn getMethodProps = (GetMethodPropsFn)importVt[VT_MI_GetMethodProps];

        unsigned int injectedMethod = 0;
        void* hTdEnum = NULL;
        unsigned int typeDefs[32];
        ULONG tdCount = 0;

        hr = enumTypeDefs(pImport, &hTdEnum, typeDefs, 32, &tdCount);
        while (hr == 0 && tdCount > 0 && !injectedMethod) {
            for (ULONG t = 0; t < tdCount && !injectedMethod; t++) {
                void* hMdEnum = NULL;
                unsigned int methods[32];
                ULONG mdCount = 0;

                hr = enumMethods(pImport, &hMdEnum, typeDefs[t], methods, 32, &mdCount);
                while (hr == 0 && mdCount > 0 && !injectedMethod) {
                    for (ULONG m = 0; m < mdCount && !injectedMethod; m++) {
                        WCHAR methodName[256];
                        memset(methodName, 0, sizeof(methodName));
                        ULONG methodNameLen = 0;
                        DWORD methodAttrs = 0;
                        ULONG codeRVA = 0;
                        DWORD implFlags = 0;

                        getMethodProps(pImport, methods[m], NULL, methodName, 256,
                                      &methodNameLen, &methodAttrs, NULL, NULL, &codeRVA, &implFlags);


                        if (codeRVA != 0 && !(methodAttrs & 0x0400) && !(implFlags & 0x0004)) {
                            char narrowMethod[512];
                            u16_to_utf8(methodName, narrowMethod, sizeof(narrowMethod));
                            PLogFmt("  Injecting into method 0x%08X: %s (RVA=0x%X)",
                                    methods[m], narrowMethod, codeRVA);

                            __sync_lock_test_and_set(&g_targetReady, 1);
                            if (DoInjectIL(moduleId, methods[m])) {
                                injectedMethod = methods[m];
                                __sync_lock_test_and_set(&g_injectionCount, 1);
                                PLog("  *** IL INJECTED FROM ModuleLoadFinished ***");
                            }
                        }
                    }
                    if (!injectedMethod) {
                        hr = enumMethods(pImport, &hMdEnum, typeDefs[t], methods, 32, &mdCount);
                    }
                }
                if (hMdEnum) closeEnum(pImport, hMdEnum);
            }
            if (!injectedMethod) {
                hr = enumTypeDefs(pImport, &hTdEnum, typeDefs, 32, &tdCount);
            }
        }
        if (hTdEnum) closeEnum(pImport, hTdEnum);

        if (!injectedMethod) {
            PLog("  WARNING: No suitable method found for injection!");
        }
    }

    SafeRelease(pEmit);
    SafeRelease(pImport);
    __sync_lock_test_and_set(&g_targetReady, 1);
    return TRUE;

fail:
    PLog("  Token creation FAILED");
    g_tokLoadFromMR = 0;
    g_tokCreateInstMR = 0;
    g_tokExceptionTR = 0;
    g_tokPathString = 0;
    g_tokTypeString = 0;
    SafeRelease(pEmit);
    SafeRelease(pImport);
    return FALSE;
}

static BOOL DoInjectIL(UINT_PTR moduleId, unsigned int methodToken) {
    void** vt = GetInfoVtable();
    HRESULT hr;

    PLogFmt("DoInjectIL: module=0x%llX method=0x%08X",
            (unsigned long long)moduleId, methodToken);


    typedef HRESULT (*GetILFunctionBodyFn)(
        void* self, UINT_PTR moduleId, unsigned int methodDef,
        BYTE** ppMethodBody, ULONG* pcbMethodSize);
    GetILFunctionBodyFn getBody = (GetILFunctionBodyFn)vt[VT_PI_GetILFunctionBody];

    BYTE* origBody = NULL;
    ULONG origSize = 0;
    hr = getBody(g_profilerInfo, moduleId, methodToken, &origBody, &origSize);
    PLogFmt("DoInjectIL: GetILFunctionBody hr=0x%08X size=%lu ptr=%p",
            hr, (unsigned long)origSize, origBody);
    if (hr != 0 || !origBody || origSize == 0) return FALSE;


    BYTE* origCode;
    ULONG origCodeSize;
    USHORT origMaxStack;
    unsigned int origLocalsSig;
    BOOL origIsTiny;
    BOOL origHasMoreSects;
    USHORT origHeaderFlags;

    if ((origBody[0] & 0x03) == CorILMethod_TinyFormat) {
        origIsTiny = TRUE;
        origCodeSize = origBody[0] >> 2;
        origCode = origBody + 1;
        origMaxStack = 8;
        origLocalsSig = 0;
        origHeaderFlags = 0;
        origHasMoreSects = FALSE;
        PLogFmt("DoInjectIL: Tiny header, codeSize=%lu", (unsigned long)origCodeSize);
    } else {
        origIsTiny = FALSE;
        origHeaderFlags = *(USHORT*)(origBody);
        origMaxStack = *(USHORT*)(origBody + 2);
        origCodeSize = *(unsigned int*)(origBody + 4);
        origLocalsSig = *(unsigned int*)(origBody + 8);
        origCode = origBody + 12;
        origHasMoreSects = (origHeaderFlags & CorILMethod_MoreSects) != 0;
        PLogFmt("DoInjectIL: Fat header, flags=0x%04X maxStack=%u codeSize=%lu locals=0x%08X moreSects=%d",
                origHeaderFlags, origMaxStack, (unsigned long)origCodeSize, origLocalsSig, origHasMoreSects);
    }


    if (origHasMoreSects) {
        PLog("DoInjectIL: Method has MoreSects, skipping");
        return FALSE;
    }


    #define INJECT_SIZE 26

    BYTE injection[INJECT_SIZE];
    int p = 0;


    injection[p++] = IL_LDSTR;
    *(unsigned int*)(injection + p) = g_tokPathString; p += 4;


    injection[p++] = IL_CALL;
    *(unsigned int*)(injection + p) = g_tokLoadFromMR; p += 4;


    injection[p++] = IL_LDSTR;
    *(unsigned int*)(injection + p) = g_tokTypeString; p += 4;


    injection[p++] = IL_CALLVIRT;
    *(unsigned int*)(injection + p) = g_tokCreateInstMR; p += 4;


    injection[p++] = IL_POP;


    injection[p++] = IL_LEAVE_S;
    injection[p++] = 3;


    injection[p++] = IL_POP;
    injection[p++] = IL_LEAVE_S;
    injection[p++] = 0;

    if (p != INJECT_SIZE) {
        PLogFmt("DoInjectIL: BUG! injection size %d != expected %d", p, INJECT_SIZE);
        return FALSE;
    }


    {
        char hexbuf[256];
        int hpos = 0;
        for (int i = 0; i < INJECT_SIZE && hpos < 240; i++) {
            hpos += sprintf(hexbuf + hpos, "%02X ", injection[i]);
        }
        PLogFmt("DoInjectIL: IL bytes: %s", hexbuf);
    }


    ULONG newCodeSize = INJECT_SIZE + origCodeSize;
    USHORT newMaxStack = origMaxStack < 2 ? 2 : origMaxStack;
    ULONG headerSize = 12;


    ULONG codeEnd = headerSize + newCodeSize;
    ULONG ehPadding = (4 - (codeEnd % 4)) % 4;
    ULONG ehSectionSize = 4 + 24;
    ULONG totalSize = codeEnd + ehPadding + ehSectionSize;
    PLogFmt("DoInjectIL: newCodeSize=%lu ehPadding=%lu ehSection=%lu totalSize=%lu",
            (unsigned long)newCodeSize, (unsigned long)ehPadding,
            (unsigned long)ehSectionSize, (unsigned long)totalSize);


    typedef HRESULT (*GetAllocatorFn)(
        void* self, UINT_PTR moduleId, void** ppMalloc);
    GetAllocatorFn getAlloc = (GetAllocatorFn)vt[VT_PI_GetILFunctionBodyAllocator];

    void* pMalloc = NULL;
    hr = getAlloc(g_profilerInfo, moduleId, &pMalloc);
    PLogFmt("DoInjectIL: GetILFunctionBodyAllocator hr=0x%08X ptr=%p", hr, pMalloc);
    if (hr != 0 || !pMalloc) return FALSE;


    void** mallocVt = *(void***)pMalloc;
    typedef BYTE* (*AllocFn)(void* self, ULONG cb);
    AllocFn pfnAlloc = (AllocFn)mallocVt[3];

    BYTE* newBody = pfnAlloc(pMalloc, totalSize);
    PLogFmt("DoInjectIL: Allocated %lu bytes at %p", (unsigned long)totalSize, newBody);
    if (!newBody) {
        SafeRelease(pMalloc);
        return FALSE;
    }

    memset(newBody, 0, totalSize);


    USHORT fatFlags = (3 << 12)
                    | CorILMethod_FatFormat
                    | CorILMethod_MoreSects;

    if (!origIsTiny && (origHeaderFlags & CorILMethod_InitLocals)) {
        fatFlags |= CorILMethod_InitLocals;
    }

    *(USHORT*)(newBody + 0) = fatFlags;
    *(USHORT*)(newBody + 2) = newMaxStack;
    *(unsigned int*)(newBody + 4) = newCodeSize;
    *(unsigned int*)(newBody + 8) = origLocalsSig;

    PLogFmt("DoInjectIL: header flags=0x%04X maxStack=%u codeSize=%lu locals=0x%08X",
            fatFlags, newMaxStack, (unsigned long)newCodeSize, origLocalsSig);


    memcpy(newBody + headerSize, injection, INJECT_SIZE);
    memcpy(newBody + headerSize + INJECT_SIZE, origCode, origCodeSize);




    BYTE* ehSection = newBody + codeEnd + ehPadding;

    ehSection[0] = CorILMethod_Sect_EHTable | CorILMethod_Sect_FatFormat;
    ehSection[1] = (BYTE)(ehSectionSize & 0xFF);
    ehSection[2] = (BYTE)((ehSectionSize >> 8) & 0xFF);
    ehSection[3] = (BYTE)((ehSectionSize >> 16) & 0xFF);


    BYTE* clause = ehSection + 4;
    *(unsigned int*)(clause + 0)  = 0x00000000;
    *(unsigned int*)(clause + 4)  = 0;
    *(unsigned int*)(clause + 8)  = 23;
    *(unsigned int*)(clause + 12) = 23;
    *(unsigned int*)(clause + 16) = 3;
    *(unsigned int*)(clause + 20) = g_tokExceptionTR;

    PLogFmt("DoInjectIL: EH clause: try=[0,%u) handler=[%u,%u) catch=0x%08X",
            23, 23, 26, g_tokExceptionTR);


    typedef HRESULT (*SetILFunctionBodyFn)(
        void* self, UINT_PTR moduleId, unsigned int methodDef,
        BYTE* pbNewILMethodHeader);
    SetILFunctionBodyFn setBody = (SetILFunctionBodyFn)vt[VT_PI_SetILFunctionBody];

    hr = setBody(g_profilerInfo, moduleId, methodToken, newBody);
    PLogFmt("DoInjectIL: SetILFunctionBody hr=0x%08X", hr);

    SafeRelease(pMalloc);

    if (hr == 0) {
        PLog("DoInjectIL: *** IL INJECTION SUCCESSFUL ***");
        return TRUE;
    } else {
        PLog("DoInjectIL: SetILFunctionBody FAILED");
        return FALSE;
    }
}

static HRESULT Prof_Initialize(UprootedProfiler* self, void* pICorProfilerInfoUnk) {
    PLog("=== Uprooted Profiler Initialize (Linux) ===");
    PLogFmt("PID: %d", getpid());


    {
        char exePath[PATH_MAX];
        ssize_t len = readlink("/proc/self/exe", exePath, sizeof(exePath) - 1);
        if (len <= 0) {
            PLog("Could not read /proc/self/exe, detaching");
            return 0x80004005;
        }
        exePath[len] = '\0';


        const char* lastSlash = strrchr(exePath, '/');
        const char* exeName = lastSlash ? lastSlash + 1 : exePath;
        PLogFmt("Process: %s", exeName);

        if (strcmp(exeName, "Root") != 0) {
            PLogFmt("Not Root (got '%s'), detaching profiler", exeName);
            return 0x80004005;
        }
    }


    void** unkVtable = *(void***)pICorProfilerInfoUnk;
    typedef HRESULT (*QI_fn)(void*, const MYGUID*, void**);
    QI_fn qi = (QI_fn)unkVtable[0];

    HRESULT hr = qi(pICorProfilerInfoUnk, &IID_ICorProfilerInfo, &g_profilerInfo);
    PLogFmt("ICorProfilerInfo: hr=0x%08X ptr=%p", hr, g_profilerInfo);

    if (hr != 0 || !g_profilerInfo) {
        PLog("FATAL: Could not get ICorProfilerInfo!");
        return 0x80004005;
    }


    void** vt = GetInfoVtable();
    typedef HRESULT (*SetEventMaskFn)(void* self, DWORD dwEvents);
    SetEventMaskFn setMask = (SetEventMaskFn)vt[VT_PI_SetEventMask];


    DWORD mask = COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_MONITOR_MODULE_LOADS | 0x00080000;
    hr = setMask(g_profilerInfo, mask);
    PLogFmt("SetEventMask(0x%08X): hr=0x%08X", mask, hr);

    PLog("=== Profiler Initialize done ===");
    return 0;
}

static HRESULT Prof_Shutdown(UprootedProfiler* self) {
    PLog("Profiler Shutdown");
    if (g_logFile) { fclose(g_logFile); g_logFile = NULL; }
    return 0;
}

static HRESULT Prof_ModuleLoadFinished(UprootedProfiler* self,
                                       UINT_PTR moduleId, HRESULT hrStatus) {
    if (!g_profilerInfo) return 0;

    int32_t n = __sync_add_and_fetch(&g_moduleCount, 1);

    void** vt = GetInfoVtable();
    typedef HRESULT (*GetModuleInfoFn)(
        void* self, UINT_PTR moduleId, BYTE** ppBaseLoadAddress,
        ULONG cchName, ULONG* pcchName, WCHAR szName[], UINT_PTR* pAssemblyId);
    GetModuleInfoFn getModInfo = (GetModuleInfoFn)vt[VT_PI_GetModuleInfo];

    WCHAR modName[512];
    memset(modName, 0, sizeof(modName));
    ULONG nameLen = 0;
    UINT_PTR asmId = 0;
    HRESULT hr = getModInfo(g_profilerInfo, moduleId, NULL, 512, &nameLen, modName, &asmId);

    if (hr == 0) {
        char narrow[512];
        u16_to_utf8(modName, narrow, sizeof(narrow));


        if (n <= 20) {
            PLogFmt("Module #%d: %s (id=0x%llX)", n, narrow, (unsigned long long)moduleId);
        }


        if (u16str(modName, W_System_Private_CoreLib) != NULL) {
            g_corelibModuleId = moduleId;
            PLogFmt("CoreLib module ID: 0x%llX", (unsigned long long)moduleId);
        }


        if (!g_targetReady && moduleId != g_corelibModuleId &&
            u16ncmp(modName, W_System_Dot, 7) != 0 &&
            u16ncmp(modName, W_Microsoft_Dot, 10) != 0) {
            PLogFmt("Trying as injection target: %s", narrow);
            if (PrepareTargetModule(moduleId)) {
                PLogFmt("*** TARGET MODULE: %s ***", narrow);
            }
        }
    }

    return 0;
}

static HRESULT Prof_JITCompilationStarted(
    UprootedProfiler* self, UINT_PTR functionId, BOOL fIsSafeToBlock) {

    if (!g_profilerInfo) return 0;

    int32_t n = __sync_add_and_fetch(&g_jitCount, 1);

    if (!g_profilerInfo) return 0;
    if (g_corelibModuleId == 0) return 0;


    void** vt = GetInfoVtable();
    typedef HRESULT (*GetFunctionInfoFn)(
        void* self, UINT_PTR functionId, UINT_PTR* pClassId,
        UINT_PTR* pModuleId, unsigned int* pToken);
    GetFunctionInfoFn getFuncInfo = (GetFunctionInfoFn)vt[VT_PI_GetFunctionInfo];

    UINT_PTR classId = 0, moduleId = 0;
    unsigned int token = 0;
    HRESULT hr = getFuncInfo(g_profilerInfo, functionId, &classId, &moduleId, &token);
    if (hr != 0) return 0;


    if (n <= 10 || (g_targetReady && moduleId == g_targetModuleId)) {
        PLogFmt("JIT #%d: module=0x%llX token=0x%08X%s",
                n, (unsigned long long)moduleId, token,
                (g_targetReady && moduleId == g_targetModuleId) ? " [TARGET]" : "");
    }


    if (g_injectionCount > 0) return 0;


    if (!g_targetReady) return 0;
    if (moduleId != g_targetModuleId) return 0;


    if (__sync_val_compare_and_swap(&g_injectionCount, 0, 1) != 0) return 0;

    PLogFmt("=== Injecting into target module method 0x%08X (JIT #%d) ===", token, n);

    if (!DoInjectIL(g_targetModuleId, token)) {
        PLog("IL injection failed, will try next method in target module");
        __sync_lock_test_and_set(&g_injectionCount, 0);
        return 0;
    }

    PLog("=== INJECTION COMPLETE - managed hook will load when method is called ===");
    return 0;
}

#define TOTAL_VTABLE_SIZE 128

static void* g_vtable[TOTAL_VTABLE_SIZE];

struct UprootedProfiler {
    void** vtable;
};

static UprootedProfiler g_profilerInstance;

static UprootedProfiler* CreateProfiler(void) {
    for (int i = 0; i < TOTAL_VTABLE_SIZE; i++) {
        g_vtable[i] = (void*)Stub_OK;
    }


    g_vtable[0] = (void*)Prof_QueryInterface;
    g_vtable[1] = (void*)Prof_AddRef;
    g_vtable[2] = (void*)Prof_Release;


    g_vtable[3]  = (void*)Prof_Initialize;
    g_vtable[4]  = (void*)Prof_Shutdown;
    g_vtable[14] = (void*)Prof_ModuleLoadFinished;
    g_vtable[23] = (void*)Prof_JITCompilationStarted;

    g_profilerInstance.vtable = g_vtable;
    return &g_profilerInstance;
}

typedef struct ClassFactory { void** vtable; } ClassFactory;

static HRESULT CF_QueryInterface(ClassFactory* self,
                                  const MYGUID* riid, void** ppv) {
    if (!ppv) return 0x80004003;
    if (MyIsEqualGUID(riid, &MY_IID_IUnknown) ||
        MyIsEqualGUID(riid, &MY_IID_IClassFactory)) {
        *ppv = self;
        return 0;
    }
    *ppv = NULL;
    return 0x80004002;
}

static ULONG CF_AddRef(ClassFactory* self) { return 2; }
static ULONG CF_Release(ClassFactory* self) { return 1; }

static HRESULT CF_CreateInstance(ClassFactory* self, void* outer,
                                  const MYGUID* riid, void** ppv) {
    PLog("ClassFactory::CreateInstance");
    if (outer) return 0x80040110;

    UprootedProfiler* prof = CreateProfiler();
    if (!prof) return 0x8007000E;

    HRESULT hr = Prof_QueryInterface(prof, riid, ppv);
    PLogFmt("  CreateInstance result: 0x%08X", hr);
    return hr;
}

static HRESULT CF_LockServer(ClassFactory* self, BOOL lock) { return 0; }

static void* g_cfVtable[] = {
    (void*)CF_QueryInterface,
    (void*)CF_AddRef,
    (void*)CF_Release,
    (void*)CF_CreateInstance,
    (void*)CF_LockServer
};

static ClassFactory g_classFactory = { g_cfVtable };

__attribute__((visibility("default")))
HRESULT DllGetClassObject(
    const MYGUID* rclsid, const MYGUID* riid, void** ppv) {
    PLog("DllGetClassObject called");
    if (!ppv) return 0x80004003;
    if (MyIsEqualGUID(rclsid, &CLSID_UprootedProfiler)) {
        *ppv = &g_classFactory;
        return 0;
    }
    *ppv = NULL;
    return 0x80040111;
}

__attribute__((visibility("default")))
HRESULT DllCanUnloadNow(void) {
    return 1;
}
