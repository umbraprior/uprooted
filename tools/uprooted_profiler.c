#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "kernel32.lib")
#pragma comment(lib, "shell32.lib")

#define HOOK_ENTRY_TYPE L"UprootedHook.Entry"

static WCHAR g_hookDllPath[MAX_PATH];
static WCHAR g_logFilePath[MAX_PATH];

static void InitPaths(void) {
    PWSTR localAppData = NULL;
    if (SUCCEEDED(SHGetKnownFolderPath(&FOLDERID_LocalAppData, 0, NULL, &localAppData))) {
        _snwprintf(g_hookDllPath, MAX_PATH, L"%s\\Root\\uprooted\\UprootedHook.dll", localAppData);
        _snwprintf(g_logFilePath, MAX_PATH, L"%s\\Root\\uprooted\\profiler.log", localAppData);
        CoTaskMemFree(localAppData);
    } else {

        wcscpy(g_hookDllPath, L"C:\\UprootedHook.dll");
        wcscpy(g_logFilePath, L"C:\\profiler.log");
    }
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

#define VT_MI_FindTypeDefByName   9
#define VT_MI_FindMethod         27
#define VT_MI_GetMethodProps     30

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

static volatile LONG g_pathsInitialized = 0;

static void EnsurePathsInitialized(void) {
    if (InterlockedCompareExchange(&g_pathsInitialized, 1, 0) == 0) {
        InitPaths();
    }
}

static FILE* g_logFile = NULL;

static void PLog(const char* msg) {
    EnsurePathsInitialized();
    if (!g_logFile) {
        char narrowPath[MAX_PATH];
        WideCharToMultiByte(CP_UTF8, 0, g_logFilePath, -1, narrowPath, MAX_PATH, NULL, NULL);
        g_logFile = fopen(narrowPath, "a");
    }
    if (g_logFile) {
        SYSTEMTIME st;
        GetLocalTime(&st);
        fprintf(g_logFile, "[%02d:%02d:%02d.%03d] %s\n",
                st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, msg);
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
            label, g->Data1, g->Data2, g->Data3,
            g->Data4[0], g->Data4[1], g->Data4[2], g->Data4[3],
            g->Data4[4], g->Data4[5], g->Data4[6], g->Data4[7]);
}

typedef struct UprootedProfiler UprootedProfiler;

static volatile LONG g_refCount = 1;
static void* g_profilerInfo = NULL;
static volatile LONG g_injectionCount = 0;
static volatile LONG g_jitCount = 0;
static volatile LONG g_moduleCount = 0;
static UINT_PTR g_corelibModuleId = 0;

static UINT_PTR g_targetModuleId = 0;

static unsigned int g_tokLoadFromMR = 0;
static unsigned int g_tokCreateInstMR = 0;
static unsigned int g_tokExceptionTR = 0;
static unsigned int g_tokPathString = 0;
static unsigned int g_tokTypeString = 0;

static volatile LONG g_targetReady = 0;

static void** GetInfoVtable(void) {
    return *(void***)g_profilerInfo;
}

static HRESULT __stdcall Stub_OK(void) { return 0; }

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

static HRESULT __stdcall Prof_QueryInterface(UprootedProfiler* self,
                                              const MYGUID* riid, void** ppv) {
    if (!ppv) return 0x80004003;
    if (MyIsEqualGUID(riid, &MY_IID_IUnknown) || IsProfilerCallbackGUID(riid)) {
        *ppv = self;
        InterlockedIncrement(&g_refCount);
        return 0;
    }
    LogGUID("QI: REJECTED", riid);
    *ppv = NULL;
    return 0x80004002;
}

static ULONG __stdcall Prof_AddRef(UprootedProfiler* self) {
    return InterlockedIncrement(&g_refCount);
}

static ULONG __stdcall Prof_Release(UprootedProfiler* self) {
    return InterlockedDecrement(&g_refCount);
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
        typedef ULONG (__stdcall *ReleaseFn)(void*);
        ((ReleaseFn)(*(void***)pInterface)[2])(pInterface);
    }
}

typedef HRESULT (__stdcall *GetModuleMetaDataFn)(
    void* self, UINT_PTR moduleId, DWORD openFlags,
    const MYGUID* riid, void** ppOut);

#define VT_MI_CloseEnum       3
#define VT_MI_EnumTypeRefs    8
#define VT_MI_GetTypeRefProps 14
#define VT_MI_FindTypeRef    55

#define VT_ME_DefineTypeRefByName 12

typedef void (__stdcall *CloseEnumFn)(void* self, void* hEnum);
typedef HRESULT (__stdcall *EnumTypeRefsFn)(
    void* self, void** phEnum, unsigned int rTypeRefs[], ULONG cMax, ULONG* pcTypeRefs);
typedef HRESULT (__stdcall *GetTypeRefPropsFn)(
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
            WCHAR trName[512] = {0};
            ULONG trNameLen = 0;
            unsigned int trScope = 0;
            getTypeRefProps(pImport, typeRefs[i], &trScope, trName, 512, &trNameLen);
            if (wcscmp(trName, targetName) == 0) {
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
            WCHAR trName[256] = {0};
            ULONG trNameLen = 0;
            unsigned int trScope = 0;
            getTypeRefProps(pImport, typeRefs[i], &trScope, trName, 256, &trNameLen);
            char narrow[256];
            WideCharToMultiByte(CP_UTF8, 0, trName, -1, narrow, 256, NULL, NULL);
            PLogFmt("    TypeRef[%lu]: 0x%08X scope=0x%08X %s", i, typeRefs[i], trScope, narrow);
        }

        while (1) {
            hr = enumTypeRefs(pImport, &hEnum, typeRefs, 256, &count);
            if (hr != 0 || count == 0) break;
            total += count;
        }
    }
    if (hEnum) closeEnum(pImport, hEnum);
    PLogFmt("  Total TypeRefs: %lu", total);
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
    unsigned int tokObjectTR = SearchTypeRef(pImport, importVt, L"System.Object", &runtimeScope);
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


    unsigned int tokAssemblyTR = SearchTypeRef(pImport, importVt, L"System.Reflection.Assembly", NULL);
    if (tokAssemblyTR) {
        PLogFmt("  Found Assembly TypeRef=0x%08X", tokAssemblyTR);
    } else {

        typedef HRESULT (__stdcall *DefineTypeRefByNameFn)(
            void* self, unsigned int tkResolutionScope, LPCWSTR szName,
            unsigned int* ptr);
        DefineTypeRefByNameFn defineTypeRef = (DefineTypeRefByNameFn)emitVt[VT_ME_DefineTypeRefByName];

        hr = defineTypeRef(pEmit, runtimeScope, L"System.Reflection.Assembly", &tokAssemblyTR);
        PLogFmt("  DefineTypeRef Assembly hr=0x%08X token=0x%08X", hr, tokAssemblyTR);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (__stdcall *DefineMemberRefFn)(
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

        hr = defineMemberRef(pEmit, tokAssemblyTR, L"LoadFrom",
                             sig, (ULONG)len, &g_tokLoadFromMR);
        PLogFmt("  LoadFrom MemberRef hr=0x%08X token=0x%08X (sigLen=%d)",
                hr, g_tokLoadFromMR, len);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (__stdcall *DefineMemberRefFn)(
            void* self, unsigned int tkImport, LPCWSTR szName,
            const BYTE* pvSigBlob, ULONG cbSigBlob,
            unsigned int* pmr);
        DefineMemberRefFn defineMemberRef = (DefineMemberRefFn)emitVt[VT_ME_DefineMemberRef];


        BYTE sig[] = { 0x20, 0x01, 0x1C, 0x0E };
        hr = defineMemberRef(pEmit, tokAssemblyTR, L"CreateInstance",
                             sig, sizeof(sig), &g_tokCreateInstMR);
        PLogFmt("  CreateInstance MemberRef hr=0x%08X token=0x%08X",
                hr, g_tokCreateInstMR);
        if (hr != 0) goto fail;
    }


    g_tokExceptionTR = SearchTypeRef(pImport, importVt, L"System.Exception", NULL);
    if (g_tokExceptionTR) {
        PLogFmt("  Found Exception TypeRef=0x%08X", g_tokExceptionTR);
    } else {
        typedef HRESULT (__stdcall *DefineTypeRefByNameFn)(
            void* self, unsigned int tkResolutionScope, LPCWSTR szName,
            unsigned int* ptr);
        DefineTypeRefByNameFn defineTypeRef = (DefineTypeRefByNameFn)emitVt[VT_ME_DefineTypeRefByName];
        hr = defineTypeRef(pEmit, runtimeScope, L"System.Exception", &g_tokExceptionTR);
        PLogFmt("  DefineTypeRef Exception hr=0x%08X token=0x%08X", hr, g_tokExceptionTR);
        if (hr != 0) goto fail;
    }


    {
        typedef HRESULT (__stdcall *DefineUserStringFn)(
            void* self, LPCWSTR szString, ULONG cchString,
            unsigned int* pstk);
        DefineUserStringFn defineStr = (DefineUserStringFn)emitVt[VT_ME_DefineUserString];

        hr = defineStr(pEmit, g_hookDllPath, (ULONG)wcslen(g_hookDllPath), &g_tokPathString);
        PLogFmt("  PathString hr=0x%08X token=0x%08X", hr, g_tokPathString);
        if (hr != 0) goto fail;

        hr = defineStr(pEmit, HOOK_ENTRY_TYPE, (ULONG)wcslen(HOOK_ENTRY_TYPE), &g_tokTypeString);
        PLogFmt("  TypeString hr=0x%08X token=0x%08X", hr, g_tokTypeString);
        if (hr != 0) goto fail;
    }

    g_targetModuleId = moduleId;
    PLog("  ALL tokens created successfully!");


    {
        typedef HRESULT (__stdcall *EnumTypeDefsFn)(
            void* self, void** phEnum, unsigned int rTypeDefs[], ULONG cMax, ULONG* pcTypeDefs);
        typedef HRESULT (__stdcall *EnumMethodsFn)(
            void* self, void** phEnum, unsigned int cl, unsigned int rMethods[], ULONG cMax, ULONG* pcTokens);
        typedef HRESULT (__stdcall *GetMethodPropsFn)(
            void* self, unsigned int mb, unsigned int* pClass, WCHAR* szMethod,
            ULONG cchMethod, ULONG* pchMethod, DWORD* pdwAttr,
            void** ppvSigBlob, ULONG* pcbSigBlob, ULONG* pulCodeRVA, DWORD* pdwImplFlags);

        #define VT_MI_EnumTypeDefs    6
        #define VT_MI_EnumMethods    18

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

                        WCHAR methodName[256] = {0};
                        ULONG methodNameLen = 0;
                        DWORD methodAttrs = 0;
                        ULONG codeRVA = 0;
                        DWORD implFlags = 0;

                        getMethodProps(pImport, methods[m], NULL, methodName, 256,
                                      &methodNameLen, &methodAttrs, NULL, NULL, &codeRVA, &implFlags);


                        if (codeRVA != 0 && !(methodAttrs & 0x0400) && !(implFlags & 0x0004)) {
                            char narrowMethod[256];
                            WideCharToMultiByte(CP_UTF8, 0, methodName, -1, narrowMethod, 256, NULL, NULL);
                            PLogFmt("  Injecting into method 0x%08X: %s (RVA=0x%X)",
                                    methods[m], narrowMethod, codeRVA);

                            InterlockedExchange(&g_targetReady, 1);
                            if (DoInjectIL(moduleId, methods[m])) {
                                injectedMethod = methods[m];
                                InterlockedExchange(&g_injectionCount, 1);
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
    InterlockedExchange(&g_targetReady, 1);
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


    typedef HRESULT (__stdcall *GetILFunctionBodyFn)(
        void* self, UINT_PTR moduleId, unsigned int methodDef,
        BYTE** ppMethodBody, ULONG* pcbMethodSize);
    GetILFunctionBodyFn getBody = (GetILFunctionBodyFn)vt[VT_PI_GetILFunctionBody];

    BYTE* origBody = NULL;
    ULONG origSize = 0;
    hr = getBody(g_profilerInfo, moduleId, methodToken, &origBody, &origSize);
    PLogFmt("DoInjectIL: GetILFunctionBody hr=0x%08X size=%lu ptr=%p",
            hr, origSize, origBody);
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
        PLogFmt("DoInjectIL: Tiny header, codeSize=%lu", origCodeSize);
    } else {
        origIsTiny = FALSE;
        origHeaderFlags = *(USHORT*)(origBody);
        origMaxStack = *(USHORT*)(origBody + 2);
        origCodeSize = *(unsigned int*)(origBody + 4);
        origLocalsSig = *(unsigned int*)(origBody + 8);
        origCode = origBody + 12;
        origHasMoreSects = (origHeaderFlags & CorILMethod_MoreSects) != 0;
        PLogFmt("DoInjectIL: Fat header, flags=0x%04X maxStack=%u codeSize=%lu locals=0x%08X moreSects=%d",
                origHeaderFlags, origMaxStack, origCodeSize, origLocalsSig, origHasMoreSects);
    }


    if (origHasMoreSects) {
        PLog("DoInjectIL: Method has MoreSects, skipping");
        return FALSE;
    }


    #define INJECT_SIZE 26

    BYTE injection[INJECT_SIZE];
    int p = 0;

#if DIAGNOSTIC_NOP_ONLY

    PLog("DoInjectIL: *** DIAGNOSTIC NOP MODE ***");
    for (p = 0; p < INJECT_SIZE; p++) injection[p] = IL_NOP;
#else

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
#endif

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
            newCodeSize, ehPadding, ehSectionSize, totalSize);


    typedef HRESULT (__stdcall *GetAllocatorFn)(
        void* self, UINT_PTR moduleId, void** ppMalloc);
    GetAllocatorFn getAlloc = (GetAllocatorFn)vt[VT_PI_GetILFunctionBodyAllocator];

    void* pMalloc = NULL;
    hr = getAlloc(g_profilerInfo, moduleId, &pMalloc);
    PLogFmt("DoInjectIL: GetILFunctionBodyAllocator hr=0x%08X ptr=%p", hr, pMalloc);
    if (hr != 0 || !pMalloc) return FALSE;


    void** mallocVt = *(void***)pMalloc;
    typedef BYTE* (__stdcall *AllocFn)(void* self, ULONG cb);
    AllocFn pfnAlloc = (AllocFn)mallocVt[3];

    BYTE* newBody = pfnAlloc(pMalloc, totalSize);
    PLogFmt("DoInjectIL: Allocated %lu bytes at %p", totalSize, newBody);
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
            fatFlags, newMaxStack, newCodeSize, origLocalsSig);


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


    typedef HRESULT (__stdcall *SetILFunctionBodyFn)(
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

static HRESULT __stdcall Prof_Initialize(UprootedProfiler* self, void* pICorProfilerInfoUnk) {
    PLog("=== Uprooted Profiler Initialize ===");
    PLogFmt("PID: %lu", GetCurrentProcessId());


    {
        WCHAR exePath[MAX_PATH];
        GetModuleFileNameW(NULL, exePath, MAX_PATH);
        WCHAR* lastSlash = wcsrchr(exePath, L'\\');
        WCHAR* exeName = lastSlash ? lastSlash + 1 : exePath;
        char narrowName[MAX_PATH];
        WideCharToMultiByte(CP_UTF8, 0, exeName, -1, narrowName, MAX_PATH, NULL, NULL);
        PLogFmt("Process: %s", narrowName);

        if (_wcsicmp(exeName, L"Root.exe") != 0) {
            PLog("Not Root.exe, detaching profiler");
            return 0x80004005;
        }
    }


    void** unkVtable = *(void***)pICorProfilerInfoUnk;
    typedef HRESULT (__stdcall *QI_fn)(void*, const MYGUID*, void**);
    QI_fn qi = (QI_fn)unkVtable[0];

    HRESULT hr = qi(pICorProfilerInfoUnk, &IID_ICorProfilerInfo, &g_profilerInfo);
    PLogFmt("ICorProfilerInfo: hr=0x%08X ptr=%p", hr, g_profilerInfo);

    if (hr != 0 || !g_profilerInfo) {
        PLog("FATAL: Could not get ICorProfilerInfo!");
        return 0x80004005;
    }


    void** vt = GetInfoVtable();
    typedef HRESULT (__stdcall *SetEventMaskFn)(void* self, DWORD dwEvents);
    SetEventMaskFn setMask = (SetEventMaskFn)vt[VT_PI_SetEventMask];


    DWORD mask = COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_MONITOR_MODULE_LOADS | 0x00080000;
    hr = setMask(g_profilerInfo, mask);
    PLogFmt("SetEventMask(0x%08X): hr=0x%08X", mask, hr);

    PLog("=== Profiler Initialize done ===");
    return 0;
}

static HRESULT __stdcall Prof_Shutdown(UprootedProfiler* self) {
    PLog("Profiler Shutdown");
    if (g_logFile) { fclose(g_logFile); g_logFile = NULL; }
    return 0;
}

static HRESULT __stdcall Prof_ModuleLoadFinished(UprootedProfiler* self,
                                                  UINT_PTR moduleId, HRESULT hrStatus) {
    if (!g_profilerInfo) return 0;

    LONG n = InterlockedIncrement(&g_moduleCount);

    void** vt = GetInfoVtable();
    typedef HRESULT (__stdcall *GetModuleInfoFn)(
        void* self, UINT_PTR moduleId, BYTE** ppBaseLoadAddress,
        ULONG cchName, ULONG* pcchName, WCHAR szName[], UINT_PTR* pAssemblyId);
    GetModuleInfoFn getModInfo = (GetModuleInfoFn)vt[VT_PI_GetModuleInfo];

    WCHAR modName[512] = {0};
    ULONG nameLen = 0;
    UINT_PTR asmId = 0;
    HRESULT hr = getModInfo(g_profilerInfo, moduleId, NULL, 512, &nameLen, modName, &asmId);

    if (hr == 0) {
        char narrow[512];
        WideCharToMultiByte(CP_UTF8, 0, modName, -1, narrow, 512, NULL, NULL);


        if (n <= 20) {
            PLogFmt("Module #%ld: %s (id=0x%llX)", n, narrow, (unsigned long long)moduleId);
        }


        if (wcsstr(modName, L"System.Private.CoreLib") != NULL) {
            g_corelibModuleId = moduleId;
            PLogFmt("CoreLib module ID: 0x%llX", (unsigned long long)moduleId);
        }


        if (!g_targetReady && moduleId != g_corelibModuleId &&
            wcsncmp(modName, L"System.", 7) != 0 &&
            wcsncmp(modName, L"Microsoft.", 10) != 0) {
            PLogFmt("Trying as injection target: %s", narrow);
            if (PrepareTargetModule(moduleId)) {
                PLogFmt("*** TARGET MODULE: %s ***", narrow);
            }
        }
    }

    return 0;
}

static HRESULT __stdcall Prof_JITCompilationStarted(
    UprootedProfiler* self, UINT_PTR functionId, BOOL fIsSafeToBlock) {

    if (!g_profilerInfo) return 0;

    LONG n = InterlockedIncrement(&g_jitCount);

    if (!g_profilerInfo) return 0;
    if (g_corelibModuleId == 0) return 0;


    void** vt = GetInfoVtable();
    typedef HRESULT (__stdcall *GetFunctionInfoFn)(
        void* self, UINT_PTR functionId, UINT_PTR* pClassId,
        UINT_PTR* pModuleId, unsigned int* pToken);
    GetFunctionInfoFn getFuncInfo = (GetFunctionInfoFn)vt[VT_PI_GetFunctionInfo];

    UINT_PTR classId = 0, moduleId = 0;
    unsigned int token = 0;
    HRESULT hr = getFuncInfo(g_profilerInfo, functionId, &classId, &moduleId, &token);
    if (hr != 0) return 0;


    if (n <= 10 || (g_targetReady && moduleId == g_targetModuleId)) {
        PLogFmt("JIT #%ld: module=0x%llX token=0x%08X%s",
                n, (unsigned long long)moduleId, token,
                (g_targetReady && moduleId == g_targetModuleId) ? " [TARGET]" : "");
    }


    if (g_injectionCount > 0) return 0;


    if (!g_targetReady) return 0;
    if (moduleId != g_targetModuleId) return 0;


    if (InterlockedCompareExchange(&g_injectionCount, 1, 0) != 0) return 0;

    PLogFmt("=== Injecting into target module method 0x%08X (JIT #%ld) ===", token, n);


    if (!DoInjectIL(g_targetModuleId, token)) {
        PLog("IL injection failed, will try next method in target module");
        InterlockedExchange(&g_injectionCount, 0);
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

static HRESULT __stdcall CF_QueryInterface(ClassFactory* self,
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

static ULONG __stdcall CF_AddRef(ClassFactory* self) { return 2; }
static ULONG __stdcall CF_Release(ClassFactory* self) { return 1; }

static HRESULT __stdcall CF_CreateInstance(ClassFactory* self, void* outer,
                                            const MYGUID* riid, void** ppv) {
    PLog("ClassFactory::CreateInstance");
    if (outer) return 0x80040110;

    UprootedProfiler* prof = CreateProfiler();
    if (!prof) return 0x8007000E;

    HRESULT hr = Prof_QueryInterface(prof, riid, ppv);
    PLogFmt("  CreateInstance result: 0x%08X", hr);
    return hr;
}

static HRESULT __stdcall CF_LockServer(ClassFactory* self, BOOL lock) { return 0; }

static void* g_cfVtable[] = {
    (void*)CF_QueryInterface,
    (void*)CF_AddRef,
    (void*)CF_Release,
    (void*)CF_CreateInstance,
    (void*)CF_LockServer
};

static ClassFactory g_classFactory = { g_cfVtable };

HRESULT __stdcall UprootedDllGetClassObject(
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

HRESULT __stdcall UprootedDllCanUnloadNow(void) {
    return 1;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);

    }
    return TRUE;
}
