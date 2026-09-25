/* Per-slot isolation for the suspended Dark Majesty client.
   Rebuild (x86):
     vcvarsall.bat x86
     cl /nologo /LD /O1 /MT /W3 /DWIN32 SlotIsolation.c /link /NOLOGO /MACHINE:X86 /OUT:..\SlotIsolation.dll kernel32.lib
   The launcher injects this DLL and calls SlotIsolation_Install. Hooks live in
   the import table, not in the A09 code patches. client.exe on disk is untouched.
*/
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define CSIDL_PERSONAL 0x0005
#define SLOT_PATH_CHARS 1024
#define SLOT_KEY_CHARS 260

static wchar_t g_slotDocsW[SLOT_PATH_CHARS];
static wchar_t g_retailDocsW[SLOT_PATH_CHARS];
static wchar_t g_slotMapsW[SLOT_PATH_CHARS];
static wchar_t g_regSubKeyW[SLOT_KEY_CHARS];
static char g_slotDocsA[SLOT_PATH_CHARS];
static char g_retailDocsA[SLOT_PATH_CHARS];
static char g_slotMapsA[SLOT_PATH_CHARS];
static char g_regSubKeyA[SLOT_KEY_CHARS];
static char g_slotId[8];
static char g_semaphoreName[64];
static int g_installed;

typedef HANDLE (WINAPI *PFN_CreateFileA)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef HANDLE (WINAPI *PFN_CreateFileW)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef HANDLE (WINAPI *PFN_FindFirstFileA)(LPCSTR, LPWIN32_FIND_DATAA);
typedef HANDLE (WINAPI *PFN_FindFirstFileW)(LPCWSTR, LPWIN32_FIND_DATAW);
typedef LONG (WINAPI *PFN_RegOpenKeyExA)(HKEY, LPCSTR, DWORD, REGSAM, PHKEY);
typedef LONG (WINAPI *PFN_RegOpenKeyExW)(HKEY, LPCWSTR, DWORD, REGSAM, PHKEY);
typedef LONG (WINAPI *PFN_RegCreateKeyExA)(HKEY, LPCSTR, DWORD, LPSTR, DWORD, REGSAM, LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD);
typedef LONG (WINAPI *PFN_RegCreateKeyExW)(HKEY, LPCWSTR, DWORD, LPWSTR, DWORD, REGSAM, LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD);
typedef HANDLE (WINAPI *PFN_CreateSemaphoreA)(LPSECURITY_ATTRIBUTES, LONG, LONG, LPCSTR);
typedef HANDLE (WINAPI *PFN_CreateMutexA)(LPSECURITY_ATTRIBUTES, BOOL, LPCSTR);
typedef FARPROC (WINAPI *PFN_GetProcAddress)(HMODULE, LPCSTR);
typedef HRESULT (WINAPI *PFN_SHGetFolderPathA)(HWND, int, HANDLE, DWORD, LPSTR);
typedef HRESULT (WINAPI *PFN_SHGetFolderPathW)(HWND, int, HANDLE, DWORD, LPWSTR);
typedef BOOL (WINAPI *PFN_SHGetSpecialFolderPathA)(HWND, LPSTR, int, BOOL);
typedef BOOL (WINAPI *PFN_SHGetSpecialFolderPathW)(HWND, LPWSTR, int, BOOL);

static PFN_CreateFileA g_RealCreateFileA;
static PFN_CreateFileW g_RealCreateFileW;
static PFN_FindFirstFileA g_RealFindFirstFileA;
static PFN_FindFirstFileW g_RealFindFirstFileW;
static PFN_RegOpenKeyExA g_RealRegOpenKeyExA;
static PFN_RegOpenKeyExW g_RealRegOpenKeyExW;
static PFN_RegCreateKeyExA g_RealRegCreateKeyExA;
static PFN_RegCreateKeyExW g_RealRegCreateKeyExW;
static PFN_CreateSemaphoreA g_RealCreateSemaphoreA;
static PFN_CreateMutexA g_RealCreateMutexA;
static PFN_GetProcAddress g_RealGetProcAddress;
static PFN_SHGetFolderPathA g_RealSHGetFolderPathA;
static PFN_SHGetFolderPathW g_RealSHGetFolderPathW;
static PFN_SHGetSpecialFolderPathA g_RealSHGetSpecialFolderPathA;
static PFN_SHGetSpecialFolderPathW g_RealSHGetSpecialFolderPathW;

static HANDLE WINAPI Hook_CreateFileA(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
static HANDLE WINAPI Hook_CreateFileW(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
static HANDLE WINAPI Hook_FindFirstFileA(LPCSTR, LPWIN32_FIND_DATAA);
static HANDLE WINAPI Hook_FindFirstFileW(LPCWSTR, LPWIN32_FIND_DATAW);
static LONG WINAPI Hook_RegOpenKeyExA(HKEY, LPCSTR, DWORD, REGSAM, PHKEY);
static LONG WINAPI Hook_RegOpenKeyExW(HKEY, LPCWSTR, DWORD, REGSAM, PHKEY);
static LONG WINAPI Hook_RegCreateKeyExA(HKEY, LPCSTR, DWORD, LPSTR, DWORD, REGSAM, LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD);
static LONG WINAPI Hook_RegCreateKeyExW(HKEY, LPCWSTR, DWORD, LPWSTR, DWORD, REGSAM, LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD);
static HANDLE WINAPI Hook_CreateSemaphoreA(LPSECURITY_ATTRIBUTES, LONG, LONG, LPCSTR);
static HANDLE WINAPI Hook_CreateMutexA(LPSECURITY_ATTRIBUTES, BOOL, LPCSTR);
static FARPROC WINAPI Hook_GetProcAddress(HMODULE, LPCSTR);
static HRESULT WINAPI Hook_SHGetFolderPathA(HWND, int, HANDLE, DWORD, LPSTR);
static HRESULT WINAPI Hook_SHGetFolderPathW(HWND, int, HANDLE, DWORD, LPWSTR);
static BOOL WINAPI Hook_SHGetSpecialFolderPathA(HWND, LPSTR, int, BOOL);
static BOOL WINAPI Hook_SHGetSpecialFolderPathW(HWND, LPWSTR, int, BOOL);

static int IsSlash(char value)
{
    return value == '\\' || value == '/';
}

static int IsSlashW(wchar_t value)
{
    return value == L'\\' || value == L'/';
}

static char LowerA(char value)
{
    if (value >= 'A' && value <= 'Z')
    {
        return (char)(value + 32);
    }

    return value;
}

static wchar_t LowerW(wchar_t value)
{
    if (value >= L'A' && value <= L'Z')
    {
        return (wchar_t)(value + 32);
    }

    return value;
}

static void TrimTrailingSlashesA(char* value)
{
    int length;
    if (value == NULL)
    {
        return;
    }

    length = lstrlenA(value);
    while (length > 0 && IsSlash(value[length - 1]))
    {
        value[--length] = '\0';
    }
}

static void TrimTrailingSlashesW(wchar_t* value)
{
    int length;
    if (value == NULL)
    {
        return;
    }

    length = lstrlenW(value);
    while (length > 0 && IsSlashW(value[length - 1]))
    {
        value[--length] = L'\0';
    }
}

static int EqualsPathCharA(char left, char right)
{
    if (IsSlash(left) && IsSlash(right))
    {
        return 1;
    }

    return LowerA(left) == LowerA(right);
}

static int EqualsPathCharW(wchar_t left, wchar_t right)
{
    if (IsSlashW(left) && IsSlashW(right))
    {
        return 1;
    }

    return LowerW(left) == LowerW(right);
}

static int StartsWithPathA(const char* path, const char* prefix)
{
    if (path == NULL || prefix == NULL || prefix[0] == '\0')
    {
        return 0;
    }

    while (*prefix != '\0')
    {
        if (!EqualsPathCharA(*path, *prefix))
        {
            return 0;
        }

        path++;
        prefix++;
    }

    return *path == '\0' || IsSlash(*path);
}

static int StartsWithPathW(const wchar_t* path, const wchar_t* prefix)
{
    if (path == NULL || prefix == NULL || prefix[0] == L'\0')
    {
        return 0;
    }

    while (*prefix != L'\0')
    {
        if (!EqualsPathCharW(*path, *prefix))
        {
            return 0;
        }

        path++;
        prefix++;
    }

    return *path == L'\0' || IsSlashW(*path);
}

static const char* FindInputMapsA(const char* path)
{
    const char* cursor = path;
    const char* marker = "game\\ui\\inputmaps";
    if (path == NULL)
    {
        return NULL;
    }

    for (; *cursor != '\0'; cursor++)
    {
        const char* left = cursor;
        const char* right = marker;
        if (cursor != path && !IsSlash(cursor[-1]))
        {
            continue;
        }

        while (*right != '\0' && EqualsPathCharA(*left, *right))
        {
            left++;
            right++;
        }

        if (*right == '\0' && (*left == '\0' || IsSlash(*left)))
        {
            return left;
        }
    }

    return NULL;
}

static const wchar_t* FindInputMapsW(const wchar_t* path)
{
    const wchar_t* cursor = path;
    const wchar_t* marker = L"game\\ui\\inputmaps";
    if (path == NULL)
    {
        return NULL;
    }

    for (; *cursor != L'\0'; cursor++)
    {
        const wchar_t* left = cursor;
        const wchar_t* right = marker;
        if (cursor != path && !IsSlashW(cursor[-1]))
        {
            continue;
        }

        while (*right != L'\0' && EqualsPathCharW(*left, *right))
        {
            left++;
            right++;
        }

        if (*right == L'\0' && (*left == L'\0' || IsSlashW(*left)))
        {
            return left;
        }
    }

    return NULL;
}

static int CopyTextA(char* destination, int destinationChars, const char* source)
{
    int index;
    if (destination == NULL || destinationChars < 2 || source == NULL)
    {
        return 0;
    }

    for (index = 0; index < destinationChars - 1 && source[index] != '\0'; index++)
    {
        destination[index] = source[index];
    }

    if (source[index] != '\0')
    {
        destination[0] = '\0';
        return 0;
    }

    destination[index] = '\0';
    return 1;
}

static int JoinA(char* destination, int destinationChars, const char* left, const char* right)
{
    int index = 0;
    int rightIndex = 0;
    if (!CopyTextA(destination, destinationChars, left))
    {
        return 0;
    }

    while (destination[index] != '\0')
    {
        index++;
    }

    if (right != NULL && IsSlash(right[0]))
    {
        right++;
    }

    if (right != NULL && right[0] != '\0')
    {
        if (index > 0 && !IsSlash(destination[index - 1]))
        {
            if (index >= destinationChars - 1)
            {
                destination[0] = '\0';
                return 0;
            }

            destination[index++] = '\\';
        }

        for (rightIndex = 0; right[rightIndex] != '\0'; rightIndex++)
        {
            if (index >= destinationChars - 1)
            {
                destination[0] = '\0';
                return 0;
            }

            destination[index++] = right[rightIndex];
        }
    }

    destination[index] = '\0';
    return 1;
}

static int RewriteIntoA(
    const char* path,
    const char* retailDocs,
    const char* slotDocs,
    const char* slotMaps,
    char* output,
    int outputChars)
{
    const char* mapsTail;
    if (output == NULL || outputChars < 2)
    {
        return -1;
    }

    output[0] = '\0';
    if (path == NULL)
    {
        return 0;
    }

    mapsTail = FindInputMapsA(path);
    if (mapsTail != NULL && slotMaps != NULL && slotMaps[0] != '\0')
    {
        return JoinA(output, outputChars, slotMaps, mapsTail) ? 1 : -1;
    }

    if (retailDocs != NULL && slotDocs != NULL && retailDocs[0] != '\0' && slotDocs[0] != '\0' &&
        StartsWithPathA(path, retailDocs))
    {
        return JoinA(output, outputChars, slotDocs, path + lstrlenA(retailDocs)) ? 1 : -1;
    }

    return CopyTextA(output, outputChars, path) ? 0 : -1;
}

__declspec(dllexport) int __cdecl SlotIsolation_RewriteA(
    const char* path,
    const char* retailDocs,
    const char* slotDocs,
    const char* slotMaps,
    char* output,
    int outputChars)
{
    return RewriteIntoA(path, retailDocs, slotDocs, slotMaps, output, outputChars);
}

static const char* RewriteOwnedA(const char* path, char* scratch, int scratchChars)
{
    int rewritten;
    if (path == NULL)
    {
        return NULL;
    }

    rewritten = RewriteIntoA(path, g_retailDocsA, g_slotDocsA, g_slotMapsA, scratch, scratchChars);
    if (rewritten == 1)
    {
        return scratch;
    }

    return path;
}

static const wchar_t* RewriteOwnedW(const wchar_t* path, wchar_t* scratch, int scratchChars)
{
    char narrow[SLOT_PATH_CHARS];
    char rewritten[SLOT_PATH_CHARS];
    int converted;
    int result;
    if (path == NULL)
    {
        return NULL;
    }

    converted = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, path, -1, narrow, SLOT_PATH_CHARS, NULL, NULL);
    if (converted <= 0)
    {
        return path;
    }

    result = RewriteIntoA(narrow, g_retailDocsA, g_slotDocsA, g_slotMapsA, rewritten, SLOT_PATH_CHARS);
    if (result != 1)
    {
        return path;
    }

    if (MultiByteToWideChar(CP_ACP, 0, rewritten, -1, scratch, scratchChars) <= 0)
    {
        return path;
    }

    return scratch;
}

static int IsGraphicsSubKeyA(const char* subKey)
{
    const char* cursor;
    const char* primary = "SOFTWARE\\Microsoft\\Microsoft Games\\Asheron's Call\\1.00";
    const char* wow = "SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft Games\\Asheron's Call\\1.00";
    if (subKey == NULL)
    {
        return 0;
    }

    cursor = subKey;
    while (IsSlash(*cursor))
    {
        cursor++;
    }

    return lstrcmpiA(cursor, primary) == 0 || lstrcmpiA(cursor, wow) == 0;
}

static int IsGraphicsSubKeyW(const wchar_t* subKey)
{
    char narrow[SLOT_KEY_CHARS];
    if (subKey == NULL)
    {
        return 0;
    }

    if (WideCharToMultiByte(CP_ACP, 0, subKey, -1, narrow, SLOT_KEY_CHARS, NULL, NULL) <= 0)
    {
        return 0;
    }

    return IsGraphicsSubKeyA(narrow);
}

static int IsSingletonNameA(const char* name)
{
    return name != NULL && lstrcmpiA(name, "Empyrean Client") == 0;
}

static int EndsWithI(const char* text, const char* suffix)
{
    int textLength;
    int suffixLength;
    int index;
    if (text == NULL || suffix == NULL)
    {
        return 0;
    }

    textLength = lstrlenA(text);
    suffixLength = lstrlenA(suffix);
    if (textLength < suffixLength)
    {
        return 0;
    }

    for (index = 0; index < suffixLength; index++)
    {
        if (LowerA(text[textLength - suffixLength + index]) != LowerA(suffix[index]))
        {
            return 0;
        }
    }

    return 1;
}

static int EndsWithIW(const wchar_t* text, const wchar_t* suffix)
{
    int textLength;
    int suffixLength;
    int index;
    if (text == NULL || suffix == NULL)
    {
        return 0;
    }

    textLength = lstrlenW(text);
    suffixLength = lstrlenW(suffix);
    if (textLength < suffixLength)
    {
        return 0;
    }

    for (index = 0; index < suffixLength; index++)
    {
        if (LowerW(text[textLength - suffixLength + index]) != LowerW(suffix[index]))
        {
            return 0;
        }
    }

    return 1;
}

/* portal.dat and cell.dat are opened read/write while only shared for reading.
   A second client then fails that open and reports the files as missing. */
static DWORD ShareDataFileA(const char* path, DWORD share)
{
    if (EndsWithI(path, ".dat"))
    {
        return share | FILE_SHARE_READ | FILE_SHARE_WRITE;
    }

    return share;
}

static DWORD ShareDataFileW(const wchar_t* path, DWORD share)
{
    if (EndsWithIW(path, L".dat"))
    {
        return share | FILE_SHARE_READ | FILE_SHARE_WRITE;
    }

    return share;
}

static HANDLE WINAPI Hook_CreateFileA(
    LPCSTR name,
    DWORD access,
    DWORD share,
    LPSECURITY_ATTRIBUTES security,
    DWORD disposition,
    DWORD flags,
    HANDLE templateFile)
{
    char rewritten[SLOT_PATH_CHARS];
    const char* opened = RewriteOwnedA(name, rewritten, SLOT_PATH_CHARS);
    return g_RealCreateFileA(
        opened,
        access,
        ShareDataFileA(opened, share),
        security,
        disposition,
        flags,
        templateFile);
}

static HANDLE WINAPI Hook_CreateFileW(
    LPCWSTR name,
    DWORD access,
    DWORD share,
    LPSECURITY_ATTRIBUTES security,
    DWORD disposition,
    DWORD flags,
    HANDLE templateFile)
{
    wchar_t rewritten[SLOT_PATH_CHARS];
    const wchar_t* opened = RewriteOwnedW(name, rewritten, SLOT_PATH_CHARS);
    return g_RealCreateFileW(
        opened,
        access,
        ShareDataFileW(opened, share),
        security,
        disposition,
        flags,
        templateFile);
}

static HANDLE WINAPI Hook_FindFirstFileA(LPCSTR name, LPWIN32_FIND_DATAA data)
{
    char rewritten[SLOT_PATH_CHARS];
    return g_RealFindFirstFileA(RewriteOwnedA(name, rewritten, SLOT_PATH_CHARS), data);
}

static HANDLE WINAPI Hook_FindFirstFileW(LPCWSTR name, LPWIN32_FIND_DATAW data)
{
    wchar_t rewritten[SLOT_PATH_CHARS];
    return g_RealFindFirstFileW(RewriteOwnedW(name, rewritten, SLOT_PATH_CHARS), data);
}

static LONG WINAPI Hook_RegOpenKeyExA(
    HKEY key,
    LPCSTR subKey,
    DWORD options,
    REGSAM sam,
    PHKEY result)
{
    if (IsGraphicsSubKeyA(subKey))
    {
        return g_RealRegOpenKeyExA((HKEY)(ULONG_PTR)0x80000001, g_regSubKeyA, options, sam, result);
    }

    return g_RealRegOpenKeyExA(key, subKey, options, sam, result);
}

static LONG WINAPI Hook_RegOpenKeyExW(
    HKEY key,
    LPCWSTR subKey,
    DWORD options,
    REGSAM sam,
    PHKEY result)
{
    if (IsGraphicsSubKeyW(subKey))
    {
        return g_RealRegOpenKeyExW((HKEY)(ULONG_PTR)0x80000001, g_regSubKeyW, options, sam, result);
    }

    return g_RealRegOpenKeyExW(key, subKey, options, sam, result);
}

static LONG WINAPI Hook_RegCreateKeyExA(
    HKEY key,
    LPCSTR subKey,
    DWORD reserved,
    LPSTR className,
    DWORD options,
    REGSAM sam,
    LPSECURITY_ATTRIBUTES security,
    PHKEY result,
    LPDWORD disposition)
{
    if (IsGraphicsSubKeyA(subKey))
    {
        return g_RealRegCreateKeyExA(
            (HKEY)(ULONG_PTR)0x80000001,
            g_regSubKeyA,
            reserved,
            className,
            options,
            sam,
            security,
            result,
            disposition);
    }

    return g_RealRegCreateKeyExA(key, subKey, reserved, className, options, sam, security, result, disposition);
}

static LONG WINAPI Hook_RegCreateKeyExW(
    HKEY key,
    LPCWSTR subKey,
    DWORD reserved,
    LPWSTR className,
    DWORD options,
    REGSAM sam,
    LPSECURITY_ATTRIBUTES security,
    PHKEY result,
    LPDWORD disposition)
{
    if (IsGraphicsSubKeyW(subKey))
    {
        return g_RealRegCreateKeyExW(
            (HKEY)(ULONG_PTR)0x80000001,
            g_regSubKeyW,
            reserved,
            className,
            options,
            sam,
            security,
            result,
            disposition);
    }

    return g_RealRegCreateKeyExW(key, subKey, reserved, className, options, sam, security, result, disposition);
}

static HANDLE WINAPI Hook_CreateSemaphoreA(
    LPSECURITY_ATTRIBUTES security,
    LONG initial,
    LONG maximum,
    LPCSTR name)
{
    if (IsSingletonNameA(name))
    {
        return g_RealCreateSemaphoreA(security, initial, maximum, g_semaphoreName);
    }

    return g_RealCreateSemaphoreA(security, initial, maximum, name);
}

static HANDLE WINAPI Hook_CreateMutexA(
    LPSECURITY_ATTRIBUTES security,
    BOOL owner,
    LPCSTR name)
{
    if (IsSingletonNameA(name))
    {
        return g_RealCreateMutexA(security, owner, g_semaphoreName);
    }

    return g_RealCreateMutexA(security, owner, name);
}

static int IsPersonalFolder(int csidl)
{
    return (csidl & 0xFF) == CSIDL_PERSONAL;
}

static HRESULT WINAPI Hook_SHGetFolderPathA(
    HWND window,
    int csidl,
    HANDLE token,
    DWORD flags,
    LPSTR path)
{
    if (IsPersonalFolder(csidl) && path != NULL && g_slotDocsA[0] != '\0')
    {
        lstrcpynA(path, g_slotDocsA, MAX_PATH);
        return S_OK;
    }

    if (g_RealSHGetFolderPathA == NULL)
    {
        return E_FAIL;
    }

    return g_RealSHGetFolderPathA(window, csidl, token, flags, path);
}

static HRESULT WINAPI Hook_SHGetFolderPathW(
    HWND window,
    int csidl,
    HANDLE token,
    DWORD flags,
    LPWSTR path)
{
    if (IsPersonalFolder(csidl) && path != NULL && g_slotDocsW[0] != L'\0')
    {
        lstrcpynW(path, g_slotDocsW, MAX_PATH);
        return S_OK;
    }

    if (g_RealSHGetFolderPathW == NULL)
    {
        return E_FAIL;
    }

    return g_RealSHGetFolderPathW(window, csidl, token, flags, path);
}

static BOOL WINAPI Hook_SHGetSpecialFolderPathA(HWND window, LPSTR path, int csidl, BOOL create)
{
    if (IsPersonalFolder(csidl) && path != NULL && g_slotDocsA[0] != '\0')
    {
        lstrcpynA(path, g_slotDocsA, MAX_PATH);
        return TRUE;
    }

    if (g_RealSHGetSpecialFolderPathA == NULL)
    {
        return FALSE;
    }

    return g_RealSHGetSpecialFolderPathA(window, path, csidl, create);
}

static BOOL WINAPI Hook_SHGetSpecialFolderPathW(HWND window, LPWSTR path, int csidl, BOOL create)
{
    if (IsPersonalFolder(csidl) && path != NULL && g_slotDocsW[0] != L'\0')
    {
        lstrcpynW(path, g_slotDocsW, MAX_PATH);
        return TRUE;
    }

    if (g_RealSHGetSpecialFolderPathW == NULL)
    {
        return FALSE;
    }

    return g_RealSHGetSpecialFolderPathW(window, path, csidl, create);
}

static FARPROC WINAPI Hook_GetProcAddress(HMODULE module, LPCSTR name)
{
    FARPROC resolved = g_RealGetProcAddress(module, name);
    if (name == NULL || ((ULONG_PTR)name >> 16) == 0)
    {
        return resolved;
    }

    if (lstrcmpiA(name, "SHGetFolderPathA") == 0)
    {
        return (FARPROC)Hook_SHGetFolderPathA;
    }

    if (lstrcmpiA(name, "SHGetFolderPathW") == 0)
    {
        return (FARPROC)Hook_SHGetFolderPathW;
    }

    if (lstrcmpiA(name, "SHGetSpecialFolderPathA") == 0)
    {
        return (FARPROC)Hook_SHGetSpecialFolderPathA;
    }

    if (lstrcmpiA(name, "SHGetSpecialFolderPathW") == 0)
    {
        return (FARPROC)Hook_SHGetSpecialFolderPathW;
    }

    return resolved;
}

static int PatchImport(HMODULE module, const char* dllName, const char* functionName, void* hook, void** original)
{
    BYTE* base = (BYTE*)module;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    IMAGE_NT_HEADERS* nt;
    IMAGE_IMPORT_DESCRIPTOR* descriptor;
    DWORD importRva;
    if (module == NULL || dos->e_magic != IMAGE_DOS_SIGNATURE)
    {
        return 0;
    }

    nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE)
    {
        return 0;
    }

    importRva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (importRva == 0)
    {
        return 0;
    }

    for (descriptor = (IMAGE_IMPORT_DESCRIPTOR*)(base + importRva); descriptor->Name != 0; descriptor++)
    {
        const char* importedDll = (const char*)(base + descriptor->Name);
        IMAGE_THUNK_DATA* originalThunk;
        IMAGE_THUNK_DATA* iat;
        if (lstrcmpiA(importedDll, dllName) != 0)
        {
            continue;
        }

        originalThunk = (IMAGE_THUNK_DATA*)(base + (descriptor->OriginalFirstThunk
            ? descriptor->OriginalFirstThunk
            : descriptor->FirstThunk));
        iat = (IMAGE_THUNK_DATA*)(base + descriptor->FirstThunk);
        for (; originalThunk->u1.AddressOfData != 0; originalThunk++, iat++)
        {
            IMAGE_IMPORT_BY_NAME* importName;
            DWORD oldProtect;
            if (IMAGE_SNAP_BY_ORDINAL(originalThunk->u1.Ordinal))
            {
                continue;
            }

            importName = (IMAGE_IMPORT_BY_NAME*)(base + originalThunk->u1.AddressOfData);
            if (lstrcmpiA((const char*)importName->Name, functionName) != 0)
            {
                continue;
            }

            if (!VirtualProtect(&iat->u1.Function, sizeof(iat->u1.Function), PAGE_READWRITE, &oldProtect))
            {
                return 0;
            }

            if (original != NULL && *original == NULL)
            {
                *original = (void*)(ULONG_PTR)iat->u1.Function;
            }

            iat->u1.Function = (ULONG_PTR)hook;
            VirtualProtect(&iat->u1.Function, sizeof(iat->u1.Function), oldProtect, &oldProtect);
            return 1;
        }
    }

    return 0;
}

static int ReadEnvA(const wchar_t* name, char* destination, int destinationChars)
{
    wchar_t wide[SLOT_PATH_CHARS];
    DWORD copied = GetEnvironmentVariableW(name, wide, SLOT_PATH_CHARS);
    if (copied == 0 || copied >= SLOT_PATH_CHARS)
    {
        destination[0] = '\0';
        return 0;
    }

    if (WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, wide, -1, destination, destinationChars, NULL, NULL) <= 0)
    {
        destination[0] = '\0';
        return 0;
    }

    return 1;
}

static int ReadEnvW(const wchar_t* name, wchar_t* destination, int destinationChars)
{
    DWORD copied = GetEnvironmentVariableW(name, destination, (DWORD)destinationChars);
    return copied > 0 && copied < (DWORD)destinationChars;
}

static int InstallHooks(void)
{
    HMODULE client = GetModuleHandleW(NULL);
    HMODULE shell32;
    if (g_installed)
    {
        return 1;
    }

    if (!ReadEnvA(L"AETHERIUM_SLOT_ID", g_slotId, (int)sizeof(g_slotId)) ||
        (lstrcmpA(g_slotId, "1") != 0 && lstrcmpA(g_slotId, "2") != 0) ||
        !ReadEnvW(L"AETHERIUM_SLOT_DOCS", g_slotDocsW, SLOT_PATH_CHARS) ||
        !ReadEnvW(L"AETHERIUM_RETAIL_DOCS", g_retailDocsW, SLOT_PATH_CHARS) ||
        !ReadEnvW(L"AETHERIUM_SLOT_MAPS", g_slotMapsW, SLOT_PATH_CHARS) ||
        !ReadEnvW(L"AETHERIUM_REG_SUBKEY", g_regSubKeyW, SLOT_KEY_CHARS) ||
        !ReadEnvA(L"AETHERIUM_SLOT_DOCS", g_slotDocsA, SLOT_PATH_CHARS) ||
        !ReadEnvA(L"AETHERIUM_RETAIL_DOCS", g_retailDocsA, SLOT_PATH_CHARS) ||
        !ReadEnvA(L"AETHERIUM_SLOT_MAPS", g_slotMapsA, SLOT_PATH_CHARS) ||
        !ReadEnvA(L"AETHERIUM_REG_SUBKEY", g_regSubKeyA, SLOT_KEY_CHARS))
    {
        return 0;
    }

    if (lstrlenA(g_slotDocsA) >= MAX_PATH || lstrlenW(g_slotDocsW) >= MAX_PATH)
    {
        return 0;
    }

    TrimTrailingSlashesA(g_slotDocsA);
    TrimTrailingSlashesA(g_retailDocsA);
    TrimTrailingSlashesA(g_slotMapsA);
    TrimTrailingSlashesW(g_slotDocsW);
    TrimTrailingSlashesW(g_retailDocsW);
    TrimTrailingSlashesW(g_slotMapsW);
    lstrcpyA(g_semaphoreName, "Empyrean Client Slot ");
    lstrcatA(g_semaphoreName, g_slotId);

    shell32 = LoadLibraryA("shell32.dll");
    if (shell32 != NULL)
    {
        g_RealSHGetFolderPathA = (PFN_SHGetFolderPathA)GetProcAddress(shell32, "SHGetFolderPathA");
        g_RealSHGetFolderPathW = (PFN_SHGetFolderPathW)GetProcAddress(shell32, "SHGetFolderPathW");
        g_RealSHGetSpecialFolderPathA = (PFN_SHGetSpecialFolderPathA)GetProcAddress(shell32, "SHGetSpecialFolderPathA");
        g_RealSHGetSpecialFolderPathW = (PFN_SHGetSpecialFolderPathW)GetProcAddress(shell32, "SHGetSpecialFolderPathW");
    }

    if (!PatchImport(client, "KERNEL32.dll", "CreateFileA", Hook_CreateFileA, (void**)&g_RealCreateFileA) ||
        g_RealCreateFileA == NULL ||
        !PatchImport(client, "ADVAPI32.dll", "RegOpenKeyExA", Hook_RegOpenKeyExA, (void**)&g_RealRegOpenKeyExA) ||
        g_RealRegOpenKeyExA == NULL)
    {
        return 0;
    }

    PatchImport(client, "KERNEL32.dll", "CreateFileW", Hook_CreateFileW, (void**)&g_RealCreateFileW);
    PatchImport(client, "KERNEL32.dll", "FindFirstFileA", Hook_FindFirstFileA, (void**)&g_RealFindFirstFileA);
    PatchImport(client, "KERNEL32.dll", "FindFirstFileW", Hook_FindFirstFileW, (void**)&g_RealFindFirstFileW);
    PatchImport(client, "ADVAPI32.dll", "RegOpenKeyExW", Hook_RegOpenKeyExW, (void**)&g_RealRegOpenKeyExW);
    PatchImport(client, "ADVAPI32.dll", "RegCreateKeyExA", Hook_RegCreateKeyExA, (void**)&g_RealRegCreateKeyExA);
    PatchImport(client, "ADVAPI32.dll", "RegCreateKeyExW", Hook_RegCreateKeyExW, (void**)&g_RealRegCreateKeyExW);
    PatchImport(client, "KERNEL32.dll", "CreateSemaphoreA", Hook_CreateSemaphoreA, (void**)&g_RealCreateSemaphoreA);
    PatchImport(client, "KERNEL32.dll", "CreateMutexA", Hook_CreateMutexA, (void**)&g_RealCreateMutexA);
    PatchImport(client, "KERNEL32.dll", "GetProcAddress", Hook_GetProcAddress, (void**)&g_RealGetProcAddress);
    g_installed = 1;
    return 1;
}

__declspec(dllexport) DWORD WINAPI SlotIsolation_Install(LPVOID unused)
{
    (void)unused;
    return InstallHooks() ? 1u : 0u;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

#pragma comment(linker, "/EXPORT:SlotIsolation_Install=_SlotIsolation_Install@4")
