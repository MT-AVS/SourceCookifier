#include <windows.h>
#include <stdio.h>
#include <wchar.h>

int wmain(int argc, wchar_t **argv)
{
    const char *names[] = {
        "isUnicode", "setInfo", "getFuncsArray",
        "messageProc", "getName", "beNotified"
    };
    HMODULE library;
    FARPROC entries[6];
    unsigned int i;

    if (argc != 2) {
        fwprintf(stderr, L"Expected a DLL path\n");
        return 1;
    }
    library = LoadLibraryW(argv[1]);
    if (library == NULL) {
        fwprintf(stderr, L"LoadLibrary failed: %lu\n", GetLastError());
        return 1;
    }
    for (i = 0; i < 6; ++i) {
        entries[i] = GetProcAddress(library, names[i]);
        if (entries[i] == NULL) {
            fprintf(stderr, "Missing export: %s\n", names[i]);
            return 1;
        }
    }
    if (!((BOOL (__cdecl *)(void))entries[0])()) {
        fprintf(stderr, "isUnicode returned false\n");
        return 1;
    }
    {
        const wchar_t *name = ((const wchar_t *(__cdecl *)(void))entries[4])();
        if (name == NULL || wcscmp(name, L"SourceCookifier") != 0) {
            fwprintf(stderr, L"Unexpected plugin name: %ls\n", name);
            return 1;
        }
    }
    puts("Six exports loaded; isUnicode and getName succeeded.");
    FreeLibrary(library);
    return 0;
}
