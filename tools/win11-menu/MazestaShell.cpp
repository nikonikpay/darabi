// Windows 11's top-level right-click entries for the file tools (what holds it open, make pictures smaller, rename these).
// A tiny native IExplorerCommand server, loaded by Explorer through the sparse package (see build.ps1). It holds no logic: the titles and the tray program's
// path come from %LOCALAPPDATA%\Mazesta\shell\menu.txt (written by the app, UTF-8, lines "lock=...", "resize=...", "rename=...", "tray=C:\...\MazestaTray.exe"),
// and Invoke starts the same command the classic menu does: MazestaTray.exe --shell <tool> "<path>" once per selected file.
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <shlobj.h>
#include <string>
#include <vector>
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")

static const GUID kLock = { 0x7b1e4c52, 0x3a1d, 0x4f0e, { 0x9a, 0x51, 0x6d, 0x2c, 0x0f, 0x5a, 0x1a, 0x01 } };
static const GUID kResize = { 0x7b1e4c52, 0x3a1d, 0x4f0e, { 0x9a, 0x51, 0x6d, 0x2c, 0x0f, 0x5a, 0x1a, 0x02 } };
static const GUID kRename = { 0x7b1e4c52, 0x3a1d, 0x4f0e, { 0x9a, 0x51, 0x6d, 0x2c, 0x0f, 0x5a, 0x1a, 0x03 } };
static LONG g_refs = 0;

static std::wstring Setting(const wchar_t* name) {
    // beside this DLL: the packaged process cannot see the user's own AppData folders
    std::wstring out; wchar_t me[MAX_PATH]; HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCWSTR)&Setting, &self);
    if (!GetModuleFileNameW(self, me, MAX_PATH)) return out;
    PathRemoveFileSpecW(me); std::wstring file = std::wstring(me) + L"\\menu.txt";
    HANDLE h = CreateFileW(file.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return out;
    std::string raw; char buf[4096]; DWORD n;
    while (ReadFile(h, buf, sizeof buf, &n, nullptr) && n) raw.append(buf, n);
    CloseHandle(h);
    int len = MultiByteToWideChar(CP_UTF8, 0, raw.data(), (int)raw.size(), nullptr, 0);
    std::wstring text(len, L'\0'); MultiByteToWideChar(CP_UTF8, 0, raw.data(), (int)raw.size(), text.data(), len);
    std::wstring key = std::wstring(name) + L"=";
    size_t at = 0;
    while (at < text.size()) {
        size_t end = text.find(L'\n', at); if (end == std::wstring::npos) end = text.size();
        std::wstring line = text.substr(at, end - at); while (!line.empty() && (line.back() == L'\r')) line.pop_back();
        if (!line.empty() && line[0] == 0xFEFF) line.erase(0, 1);
        if (line.rfind(key, 0) == 0) return line.substr(key.size());
        at = end + 1;
    }
    return out;
}

static bool IsImage(const std::wstring& path) {
    const wchar_t* ext = PathFindExtensionW(path.c_str());
    for (const wchar_t* e : { L".jpg", L".jpeg", L".png", L".bmp", L".gif", L".tif", L".tiff", L".webp", L".heic" }) if (_wcsicmp(ext, e) == 0) return true;
    return false;
}

static std::vector<std::wstring> Paths(IShellItemArray* items) {
    std::vector<std::wstring> r; DWORD count = 0;
    if (!items || FAILED(items->GetCount(&count))) return r;
    for (DWORD i = 0; i < count; i++) {
        IShellItem* item = nullptr; if (FAILED(items->GetItemAt(i, &item))) continue;
        PWSTR p = nullptr; if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &p))) { r.push_back(p); CoTaskMemFree(p); }
        item->Release();
    }
    return r;
}

class Cmd : public IExplorerCommand {
    LONG _r = 1; std::wstring _tool, _fallback;
public:
    Cmd(const wchar_t* tool, const wchar_t* fallback) : _tool(tool), _fallback(fallback) { InterlockedIncrement(&g_refs); }
    ~Cmd() { InterlockedDecrement(&g_refs); }
    STDMETHODIMP QueryInterface(REFIID id, void** o) override {
        if (id == IID_IUnknown || id == __uuidof(IExplorerCommand)) { *o = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_r); }
    STDMETHODIMP_(ULONG) Release() override { ULONG n = InterlockedDecrement(&_r); if (!n) delete this; return n; }
    STDMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override { std::wstring t = Setting(_tool.c_str()); return SHStrDupW(t.empty() ? _fallback.c_str() : t.c_str(), name); }
    STDMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        std::wstring tray = Setting(L"tray"); if (tray.empty()) { *icon = nullptr; return E_NOTIMPL; }
        return SHStrDupW((L"\"" + tray + L"\",0").c_str(), icon);
    }
    STDMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    STDMETHODIMP GetCanonicalName(GUID* g) override { *g = GUID_NULL; return S_OK; }
    STDMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        *state = ECS_ENABLED;
        if (_tool == L"lock") { DWORD n = 0; if (!items || FAILED(items->GetCount(&n)) || n != 1) *state = ECS_HIDDEN; }   // it checks one file, not a selection
        if (_tool == L"resize") { bool any = false; for (auto& p : Paths(items)) if (IsImage(p)) any = true; if (!any) *state = ECS_HIDDEN; }
        return S_OK;
    }
    STDMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override {
        std::wstring tray = Setting(L"tray"); if (tray.empty()) return E_FAIL;
        for (auto& p : Paths(items)) {
            if (_tool == L"resize" && !IsImage(p)) continue;
            std::wstring cmd = L"\"" + tray + L"\" --shell " + _tool + L" \"" + p + L"\"";
            STARTUPINFOW si{}; si.cb = sizeof si; PROCESS_INFORMATION pi{};
            if (CreateProcessW(nullptr, cmd.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        }
        return S_OK;
    }
    STDMETHODIMP GetFlags(EXPCMDFLAGS* f) override { *f = ECF_DEFAULT; return S_OK; }
    STDMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }
};

class Factory : public IClassFactory {
    LONG _r = 1; const wchar_t *_tool, *_fallback;
public:
    Factory(const wchar_t* tool, const wchar_t* fallback) : _tool(tool), _fallback(fallback) {}
    STDMETHODIMP QueryInterface(REFIID id, void** o) override {
        if (id == IID_IUnknown || id == IID_IClassFactory) { *o = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_r); }
    STDMETHODIMP_(ULONG) Release() override { ULONG n = InterlockedDecrement(&_r); if (!n) delete this; return n; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID id, void** o) override {
        *o = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        Cmd* c = new Cmd(_tool, _fallback); HRESULT hr = c->QueryInterface(id, o); c->Release(); return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&g_refs); else InterlockedDecrement(&g_refs); return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID id, void** o) {
    Factory* f = nullptr;
    if (clsid == kLock) f = new Factory(L"lock", L"What is holding this open?");
    else if (clsid == kResize) f = new Factory(L"resize", L"Make pictures smaller");
    else if (clsid == kRename) f = new Factory(L"rename", L"Rename together");
    if (!f) { *o = nullptr; return CLASS_E_CLASSNOTAVAILABLE; }
    HRESULT hr = f->QueryInterface(id, o); f->Release(); return hr;
}
STDAPI DllCanUnloadNow() { return g_refs == 0 ? S_OK : S_FALSE; }
