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

/* DAT sharing is deliberately left at the stock client flags. Each
   account slot now runs from its own writable portal.dat/cell.dat workspace,
   so forcing FILE_SHARE_WRITE would only re-introduce cross-process corruption. */

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
        share,
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
        share,
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

/* The plugin window is not part of Turbine's plugin interface. A visible
   window stops native exclusive fullscreen and covers the chat bar. Keep that
   window off the desktop and copy its paint into the DirectDraw frame. */
typedef HRESULT (WINAPI *PFN_DirectDrawCreate)(void*, void**, void*);
typedef HRESULT (WINAPI *PFN_DirectDrawCreateEx)(void*, void**, const GUID*, void*);
typedef HRESULT (WINAPI *PFN_DdCreateSurface)(void*, void*, void**, void*);
typedef HRESULT (WINAPI *PFN_DdSetCoop)(void*, HWND, DWORD);
typedef HRESULT (WINAPI *PFN_DdGetAttached)(void*, void*, void**);
typedef HRESULT (WINAPI *PFN_DdGetDC)(void*, HDC*);
typedef HRESULT (WINAPI *PFN_DdReleaseDC)(void*, HDC);
typedef HRESULT (WINAPI *PFN_DdFlip)(void*, void*, DWORD);
typedef HWND (WINAPI *PFN_CreateWindowExW)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
typedef BOOL (WINAPI *PFN_ShowWindow)(HWND, int);
typedef BOOL (WINAPI *PFN_SetWindowPos)(HWND, HWND, int, int, int, int, UINT);
typedef HMODULE (WINAPI *PFN_LoadLibraryA)(LPCSTR);
typedef HMODULE (WINAPI *PFN_LoadLibraryW)(LPCWSTR);

static PFN_DirectDrawCreate g_RealDirectDrawCreate;
static PFN_DirectDrawCreateEx g_RealDirectDrawCreateEx;
static PFN_CreateWindowExW g_RealCreateWindowExW;
static PFN_ShowWindow g_RealShowWindow;
static PFN_SetWindowPos g_RealSetWindowPos;
typedef BOOL (WINAPI *PFN_BitBlt)(HDC, int, int, int, int, HDC, int, int, DWORD);
static PFN_BitBlt g_RealBitBlt;
static HDC g_CacheDc;
static HBITMAP g_CacheBmp;
static int g_CacheW;
static int g_CacheH;
static int g_HaveCapture;
static PFN_LoadLibraryA g_RealLoadLibraryA;
static PFN_LoadLibraryW g_RealLoadLibraryW;
static HWND g_GameWindow;
static HWND g_PluginPanel;
static HWND g_PluginTab;
static int g_PanelShown = 1;
static WNDPROC g_GameProc;
typedef HRESULT (WINAPI *PFN_DdQuery)(void*, const GUID*, void**);
typedef HRESULT (WINAPI *PFN_DdBlt)(void*, RECT*, void*, RECT*, DWORD, void*);
static struct { void** vtbl; PFN_DdQuery query; PFN_DdCreateSurface createSurface; PFN_DdSetCoop setCoop; } g_DdHook[4];
static struct { void** vtbl; PFN_DdFlip flip; PFN_DdBlt blt; } g_FlipHook[4];
static void* g_PrimarySurface;
static HRESULT WINAPI Hook_QueryInterface(void*, const GUID*, void**);
static HRESULT WINAPI Hook_Blt(void*, RECT*, void*, RECT*, DWORD, void*);

static HRESULT WINAPI Hook_DirectDrawCreate(void*, void**, void*);
static HRESULT WINAPI Hook_DirectDrawCreateEx(void*, void**, const GUID*, void*);
static HRESULT WINAPI Hook_CreateSurface(void*, void*, void**, void*);
static HRESULT WINAPI Hook_SetCooperativeLevel(void*, HWND, DWORD);
static HRESULT WINAPI Hook_Flip(void*, void*, DWORD);
static HWND WINAPI Hook_CreateWindowExW(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
static BOOL WINAPI Hook_ShowWindow(HWND, int);
static BOOL WINAPI Hook_SetWindowPos(HWND, HWND, int, int, int, int, UINT);
static BOOL WINAPI Hook_BitBlt(HDC, int, int, int, int, HDC, int, int, DWORD);
static HMODULE WINAPI Hook_LoadLibraryA(LPCSTR);
static HMODULE WINAPI Hook_LoadLibraryW(LPCWSTR);
static LRESULT CALLBACK GameOverlayProc(HWND, UINT, WPARAM, LPARAM);

static int PatchVtableSlot(void** vtbl, int slot, void* hook, void** saved)
{
    DWORD oldProtect;
    if (vtbl == NULL || vtbl[slot] == hook)
    {
        return 1;
    }

    if (!VirtualProtect(&vtbl[slot], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        return 0;
    }

    if (saved != NULL && *saved == NULL)
    {
        *saved = vtbl[slot];
    }

    vtbl[slot] = hook;
    VirtualProtect(&vtbl[slot], sizeof(void*), oldProtect, &oldProtect);
    return 1;
}

static int PatchImport(HMODULE module, const char* dllName, const char* functionName, void* hook, void** original);
static void PatchModuleWindowImports(HMODULE module)
{
    PatchImport(module, "USER32.dll", "CreateWindowExW", Hook_CreateWindowExW, (void**)&g_RealCreateWindowExW);
    PatchImport(module, "USER32.dll", "ShowWindow", Hook_ShowWindow, (void**)&g_RealShowWindow);
    PatchImport(module, "USER32.dll", "SetWindowPos", Hook_SetWindowPos, (void**)&g_RealSetWindowPos);
    PatchImport(module, "GDI32.dll", "BitBlt", Hook_BitBlt, (void**)&g_RealBitBlt);
}

static void OverlayNote(const char* text);
static void RememberPanel(HDC source, int width, int height)
{
    HDC screen;
    if (source == NULL || g_RealBitBlt == NULL || width < 32 || height < 32 || width > 2000 || height > 2000)
    {
        return;
    }

    if (g_CacheDc == NULL || width != g_CacheW || height != g_CacheH)
    {
        if (g_CacheBmp != NULL)
        {
            DeleteObject(g_CacheBmp);
            g_CacheBmp = NULL;
        }

        if (g_CacheDc != NULL)
        {
            DeleteDC(g_CacheDc);
            g_CacheDc = NULL;
        }

        screen = GetDC(NULL);
        g_CacheDc = CreateCompatibleDC(screen);
        g_CacheBmp = CreateCompatibleBitmap(screen, width, height);
        ReleaseDC(NULL, screen);
        SelectObject(g_CacheDc, g_CacheBmp);
        g_CacheW = width;
        g_CacheH = height;
    }

    g_RealBitBlt(g_CacheDc, 0, 0, width, height, source, 0, 0, SRCCOPY);
    if (!g_HaveCapture)
    {
        OverlayNote("captured panel paint");
    }

    g_HaveCapture = 1;
}

static BOOL WINAPI Hook_BitBlt(HDC dest, int x, int y, int width, int height, HDC source, int sourceX, int sourceY, DWORD rop)
{
    if (g_PluginPanel != NULL && dest != NULL && source != NULL && WindowFromDC(dest) == g_PluginPanel)
    {
        RememberPanel(source, width, height);
    }

    return g_RealBitBlt(dest, x, y, width, height, source, sourceX, sourceY, rop);
}

static void OverlayNote(const char* text)
{
    HANDLE file;
    DWORD written;
    char path[MAX_PATH];
    if (GetTempPathA(MAX_PATH, path) == 0)
    {
        return;
    }

    lstrcatA(path, "aeth-overlay.log");
    file = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        return;
    }

    WriteFile(file, text, lstrlenA(text), &written, NULL);
    WriteFile(file, "\r\n", 2, &written, NULL);
    CloseHandle(file);
}

static void TrackArrow(void);
static void DrawPluginInto(void* surface)
{
    void** vtbl;
    HDC surfaceDc = NULL;
    struct { DWORD caps; } backCaps;
    void* back = NULL;
    HRESULT dcResult;
    TrackArrow();
    if (g_PluginPanel == NULL || !g_PanelShown || !IsWindow(g_PluginPanel) || surface == NULL)
    {
        return;
    }

    vtbl = *(void***)surface;
    backCaps.caps = 0x4;
    if (((PFN_DdGetAttached)vtbl[12])(surface, &backCaps, &back) >= 0 && back != NULL)
    {
        surface = back;
        vtbl = *(void***)surface;
    }

    dcResult = ((PFN_DdGetDC)vtbl[17])(surface, &surfaceDc);
    if (dcResult < 0 || surfaceDc == NULL)
    {
        OverlayNote("surface GetDC failed");
        return;
    }

    InvalidateRect(g_PluginPanel, NULL, FALSE);
    UpdateWindow(g_PluginPanel);
    if (!g_HaveCapture || g_CacheDc == NULL)
    {
        ((PFN_DdReleaseDC)vtbl[26])(surface, surfaceDc);
        return;
    }

    BitBlt(surfaceDc, 8, 8, g_CacheW, g_CacheH, g_CacheDc, 0, 0, SRCCOPY);
    ((PFN_DdReleaseDC)vtbl[26])(surface, surfaceDc);
    (void)dcResult;
}

static void WrapPrimary(void* surface)
{
    void** vtbl;
    int index;
    if (surface == NULL)
    {
        return;
    }

    vtbl = *(void***)surface;
    for (index = 0; index < 4; index++)
    {
        if (g_FlipHook[index].vtbl == vtbl)
        {
            return;
        }
    }

    for (index = 0; index < 4 && g_FlipHook[index].vtbl != NULL; index++)
    {
    }

    if (index >= 4)
    {
        return;
    }

    g_FlipHook[index].vtbl = vtbl;
    g_FlipHook[index].flip = (PFN_DdFlip)vtbl[11];
    g_FlipHook[index].blt = (PFN_DdBlt)vtbl[5];
    g_PrimarySurface = surface;
    PatchVtableSlot(vtbl, 11, Hook_Flip, NULL);
    PatchVtableSlot(vtbl, 5, Hook_Blt, NULL);
    OverlayNote("wrapped primary surface");
}

static void WrapDirectDraw(void* object)
{
    void** vtbl;
    int index;
    if (object == NULL)
    {
        return;
    }

    vtbl = *(void***)object;
    for (index = 0; index < 4; index++)
    {
        if (g_DdHook[index].vtbl == vtbl)
        {
            return;
        }
    }

    for (index = 0; index < 4 && g_DdHook[index].vtbl != NULL; index++)
    {
    }

    if (index >= 4)
    {
        return;
    }

    g_DdHook[index].vtbl = vtbl;
    g_DdHook[index].query = (PFN_DdQuery)vtbl[0];
    g_DdHook[index].createSurface = (PFN_DdCreateSurface)vtbl[6];
    g_DdHook[index].setCoop = (PFN_DdSetCoop)vtbl[20];
    PatchVtableSlot(vtbl, 0, Hook_QueryInterface, NULL);
    PatchVtableSlot(vtbl, 6, Hook_CreateSurface, NULL);
    PatchVtableSlot(vtbl, 20, Hook_SetCooperativeLevel, NULL);
    OverlayNote("wrapped directdraw");
}

static int SameGuid(const GUID* left, DWORD data1)
{
    return left != NULL && left->Data1 == data1;
}

static HRESULT WINAPI Hook_QueryInterface(void* self, const GUID* iid, void** out)
{
    void** vtbl = self == NULL ? NULL : *(void***)self;
    PFN_DdQuery real = NULL;
    HRESULT result;
    int index;
    for (index = 0; index < 4; index++)
    {
        if (g_DdHook[index].vtbl == vtbl)
        {
            real = g_DdHook[index].query;
        }
    }

    result = real == NULL ? E_FAIL : real(self, iid, out);
    if (result >= 0 && out != NULL && *out != NULL &&
        (SameGuid(iid, 0x6C14DB80u) || SameGuid(iid, 0xB3A6F3E0u) ||
         SameGuid(iid, 0x9C59509Au) || SameGuid(iid, 0x15E65EC0u)))
    {
        WrapDirectDraw(*out);
    }

    return result;
}

static HRESULT WINAPI Hook_Blt(void* self, RECT* dest, void* source, RECT* sourceRect, DWORD flags, void* fx)
{
    void** vtbl = self == NULL ? NULL : *(void***)self;
    PFN_DdBlt real = NULL;
    int index;
    for (index = 0; index < 4; index++)
    {
        if (g_FlipHook[index].vtbl == vtbl)
        {
            real = g_FlipHook[index].blt;
        }
    }

    if (self == g_PrimarySurface || source == g_PrimarySurface)
    {
        DrawPluginInto(source != NULL ? source : self);
    }

    return real == NULL ? E_FAIL : real(self, dest, source, sourceRect, flags, fx);
}

static HRESULT WINAPI Hook_Flip(void* self, void* target, DWORD flags)
{
    void** vtbl = self == NULL ? NULL : *(void***)self;
    PFN_DdFlip real = NULL;
    int index;
    for (index = 0; index < 4; index++)
    {
        if (g_FlipHook[index].vtbl == vtbl)
        {
            real = g_FlipHook[index].flip;
        }
    }

    DrawPluginInto(self);
    return real == NULL ? E_FAIL : real(self, target, flags);
}

static HRESULT WINAPI Hook_CreateSurface(void* self, void* desc, void** surface, void* outer)
{
    void** vtbl = self == NULL ? NULL : *(void***)self;
    PFN_DdCreateSurface real = NULL;
    HRESULT result;
    int index;
    DWORD flags = 0;
    DWORD caps = 0;
    for (index = 0; index < 4; index++)
    {
        if (g_DdHook[index].vtbl == vtbl)
        {
            real = g_DdHook[index].createSurface;
        }
    }

    result = real == NULL ? E_FAIL : real(self, desc, surface, outer);
    if (result >= 0 && desc != NULL && surface != NULL && *surface != NULL)
    {
        flags = *(DWORD*)((BYTE*)desc + 4);
        caps = *(DWORD*)((BYTE*)desc + 104);
        if ((flags & 1u) != 0 && (caps & 0x200u) != 0)
        {
            WrapPrimary(*surface);
        }
        else
        {
            char note[80];
            wsprintfA(note, "surface flags=%08lX caps=%08lX", flags, caps);
            OverlayNote(note);
        }
    }

    return result;
}

static void AttachGameWindow(HWND window)
{
    if (window == NULL || !IsWindow(window) || g_GameWindow == window)
    {
        return;
    }

    g_GameWindow = window;
    g_GameProc = (WNDPROC)SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)GameOverlayProc);
}

static int g_ArrowHeld;
static HCURSOR g_Arrow;

static int OverPanel(int x, int y, int* localX, int* localY)
{
    RECT bounds;
    int width;
    int height;
    if (g_PluginPanel == NULL || !g_PanelShown || !IsWindow(g_PluginPanel) || !GetWindowRect(g_PluginPanel, &bounds))
    {
        return 0;
    }

    width = bounds.right - bounds.left;
    height = bounds.bottom - bounds.top;
    if (localX != NULL)
    {
        *localX = x - 8;
    }

    if (localY != NULL)
    {
        *localY = y - 8;
    }

    return x >= 8 && y >= 8 && x < 8 + width && y < 8 + height;
}

static void HoldArrow(void)
{
    int guard = 0;
    if (g_Arrow == NULL)
    {
        g_Arrow = LoadCursorW(NULL, MAKEINTRESOURCEW(32512));
    }

    SetCursor(g_Arrow);
    if (!g_ArrowHeld)
    {
        while (ShowCursor(TRUE) < 0 && guard < 32)
        {
            guard++;
        }

        g_ArrowHeld = 1;
    }
}

static void ReleaseArrow(void)
{
    int guard = 0;
    if (!g_ArrowHeld)
    {
        return;
    }

    while (ShowCursor(FALSE) >= 0 && guard < 32)
    {
        guard++;
    }

    g_ArrowHeld = 0;
}

static void TrackArrow(void)
{
    POINT point;
    if (g_GameWindow == NULL || !GetCursorPos(&point))
    {
        return;
    }

    ScreenToClient(g_GameWindow, &point);
    if (OverPanel(point.x, point.y, NULL, NULL))
    {
        HoldArrow();
    }
    else
    {
        ReleaseArrow();
    }
}

static LRESULT CALLBACK GameOverlayProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    POINT point;
    int localX;
    int localY;
    int over;
    if (g_PluginPanel != NULL && g_PanelShown &&
        (message == WM_MOUSEMOVE || message == WM_SETCURSOR ||
         message == WM_LBUTTONDOWN || message == WM_LBUTTONDBLCLK ||
         message == WM_LBUTTONUP || message == WM_MOUSEWHEEL))
    {
        if (message == WM_SETCURSOR)
        {
            if (!GetCursorPos(&point))
            {
                return CallWindowProcW(g_GameProc, window, message, wparam, lparam);
            }

            ScreenToClient(window, &point);
        }
        else if (message == WM_MOUSEWHEEL)
        {
            point.x = (short)LOWORD(lparam);
            point.y = (short)HIWORD(lparam);
            ScreenToClient(window, &point);
        }
        else
        {
            point.x = (short)LOWORD(lparam);
            point.y = (short)HIWORD(lparam);
        }

        over = OverPanel(point.x, point.y, &localX, &localY);
        if (over)
        {
            HoldArrow();
            if (message != WM_SETCURSOR)
            {
                SendMessageW(g_PluginPanel, message, wparam, MAKELPARAM(localX, localY));
            }

            return message == WM_SETCURSOR ? TRUE : 0;
        }

        ReleaseArrow();
    }

    return CallWindowProcW(g_GameProc, window, message, wparam, lparam);
}

static HRESULT WINAPI Hook_SetCooperativeLevel(void* self, HWND window, DWORD flags)
{
    void** vtbl = self == NULL ? NULL : *(void***)self;
    PFN_DdSetCoop real = NULL;
    int index;
    for (index = 0; index < 4; index++)
    {
        if (g_DdHook[index].vtbl == vtbl)
        {
            real = g_DdHook[index].setCoop;
        }
    }

    AttachGameWindow(window);
    return real == NULL ? E_FAIL : real(self, window, flags);
}

static HRESULT WINAPI Hook_DirectDrawCreate(void* guid, void** out, void* outer)
{
    HRESULT result = g_RealDirectDrawCreate == NULL ? E_FAIL : g_RealDirectDrawCreate(guid, out, outer);
    if (result >= 0 && out != NULL && *out != NULL)
    {
        WrapDirectDraw(*out);
    }

    return result;
}

static HRESULT WINAPI Hook_DirectDrawCreateEx(void* guid, void** out, const GUID* iid, void* outer)
{
    HRESULT result = g_RealDirectDrawCreateEx == NULL ? E_FAIL : g_RealDirectDrawCreateEx(guid, out, iid, outer);
    if (result >= 0 && out != NULL && *out != NULL)
    {
        WrapDirectDraw(*out);
    }

    return result;
}

static int IsPluginWindowClass(LPCWSTR className)
{
    return className != NULL && ((ULONG_PTR)className >> 16) != 0 &&
        (lstrcmpiW(className, L"AethTankPanel") == 0 || lstrcmpiW(className, L"AethTankTab") == 0);
}

static HWND WINAPI Hook_CreateWindowExW(
    DWORD exStyle, LPCWSTR className, LPCWSTR windowName, DWORD style,
    int x, int y, int width, int height, HWND parent, HMENU menu, HINSTANCE instance, LPVOID param)
{
    int pluginWindow = IsPluginWindowClass(className);
    HWND created;
    if (pluginWindow)
    {
        exStyle = (exStyle & ~0x8u) | 0x08000080u;
        style &= ~WS_VISIBLE;
        parent = NULL;
        x = -32000;
        y = -32000;
    }

    created = g_RealCreateWindowExW(
        exStyle, className, windowName, style, x, y, width, height, parent, menu, instance, param);
    if (pluginWindow && created != NULL)
    {
        if (lstrcmpiW(className, L"AethTankPanel") == 0)
        {
            g_PluginPanel = created;
            g_PanelShown = 1;
            OverlayNote("captured plugin panel");
        }
        else
        {
            g_PluginTab = created;
        }
    }

    return created;
}

static BOOL WINAPI Hook_ShowWindow(HWND window, int command)
{
    if (window == g_PluginPanel || window == g_PluginTab)
    {
        if (window == g_PluginPanel)
        {
            g_PanelShown = command != SW_HIDE;
        }

        if (command == SW_HIDE)
        {
            return g_RealShowWindow(window, SW_HIDE);
        }

        /* Shown, but off the monitor, so exclusive fullscreen and the chat bar stay intact. */
        g_RealSetWindowPos(window, NULL, -32000, -32000, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return g_RealShowWindow(window, SW_SHOWNA);
    }

    return g_RealShowWindow(window, command);
}

static BOOL WINAPI Hook_SetWindowPos(HWND window, HWND insertAfter, int x, int y, int width, int height, UINT flags)
{
    if (window == g_PluginPanel || window == g_PluginTab)
    {
        x = -32000;
        y = -32000;
        flags = (flags | SWP_NOACTIVATE | SWP_NOZORDER) & ~SWP_SHOWWINDOW;
    }

    return g_RealSetWindowPos(window, insertAfter, x, y, width, height, flags);
}

static void PatchLoadedModule(HMODULE module)
{
    if (module != NULL)
    {
        PatchModuleWindowImports(module);
    }
}

static HMODULE WINAPI Hook_LoadLibraryA(LPCSTR fileName)
{
    HMODULE module = g_RealLoadLibraryA(fileName);
    PatchLoadedModule(module);
    return module;
}

static HMODULE WINAPI Hook_LoadLibraryW(LPCWSTR fileName)
{
    HMODULE module = g_RealLoadLibraryW(fileName);
    PatchLoadedModule(module);
    return module;
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

    if (lstrcmpiA(name, "DirectDrawCreate") == 0)
    {
        if (g_RealDirectDrawCreate == NULL)
        {
            g_RealDirectDrawCreate = (PFN_DirectDrawCreate)resolved;
        }

        return (FARPROC)Hook_DirectDrawCreate;
    }

    if (lstrcmpiA(name, "DirectDrawCreateEx") == 0)
    {
        if (g_RealDirectDrawCreateEx == NULL)
        {
            g_RealDirectDrawCreateEx = (PFN_DirectDrawCreateEx)resolved;
        }

        return (FARPROC)Hook_DirectDrawCreateEx;
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
    PatchImport(client, "KERNEL32.dll", "LoadLibraryA", Hook_LoadLibraryA, (void**)&g_RealLoadLibraryA);
    PatchImport(client, "KERNEL32.dll", "LoadLibraryW", Hook_LoadLibraryW, (void**)&g_RealLoadLibraryW);
    PatchImport(client, "ddraw.dll", "DirectDrawCreate", Hook_DirectDrawCreate, (void**)&g_RealDirectDrawCreate);
    PatchImport(client, "ddraw.dll", "DirectDrawCreateEx", Hook_DirectDrawCreateEx, (void**)&g_RealDirectDrawCreateEx);
    PatchModuleWindowImports(client);
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
