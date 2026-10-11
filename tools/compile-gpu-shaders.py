# Compiles the raw D3D12 shaders to signed DXIL on Linux: the same jobs and flags as tools/compile-gpu-shaders.ps1 (which it reads), with
# Microsoft's DXC as Vortice.Dxc.Native ships it for Linux (libdxcompiler.so, and libdxil.so, which validates and signs), fetched once
# from NuGet into obj/dxc-linux. Run after changing a .hlsl file, on a machine without Windows:
#   python3 tools/compile-gpu-shaders.py             (every shader)
#   python3 tools/compile-gpu-shaders.py GardenLensPS.cso ...   (only these)
import ctypes as C, re, sys, os, uuid, urllib.request, zipfile, io
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
VERSION = '1.0.6'   # Vortice.Dxc.Native 1.0.6: DXC 1.9.2609
NATIVE = os.path.join(ROOT, 'obj', 'dxc-linux', VERSION)
if not os.path.exists(os.path.join(NATIVE, 'libdxcompiler.so')):
    os.makedirs(NATIVE, exist_ok=True)
    pkg = zipfile.ZipFile(io.BytesIO(urllib.request.urlopen(f'https://api.nuget.org/v3-flatcontainer/vortice.dxc.native/{VERSION}/vortice.dxc.native.{VERSION}.nupkg').read()))
    for name in ('libdxcompiler.so', 'libdxil.so'): open(os.path.join(NATIVE, name), 'wb').write(pkg.read(f'runtimes/linux-x64/native/{name}'))
C.CDLL(os.path.join(NATIVE, 'libdxil.so'), mode=C.RTLD_GLOBAL)   # loaded first, so the compiler finds it and signs what it makes
lib = C.CDLL(os.path.join(NATIVE, 'libdxcompiler.so'))
def guid(s): return (C.c_ubyte * 16).from_buffer_copy(uuid.UUID(s).bytes_le)
CLSID = guid('73e22d93-e6ce-47f3-b5bf-f0664f39c1b0'); IID_C3 = guid('228B4687-5A6A-4730-900C-9702B2203F54')
IID_RES = guid('58346CDA-DDE7-4497-9461-6F87AF5E0659'); IID_BLOB = guid('8BA5FB08-5195-40e2-AC58-0D989C3A0102')
OFF = 3   # an interface's own methods follow IUnknown's three in its table
def method(obj, index, restype, *argtypes):
    vt = C.cast(C.cast(obj, C.POINTER(C.c_void_p))[0], C.POINTER(C.c_void_p))
    return C.CFUNCTYPE(restype, C.c_void_p, *argtypes)(vt[index])
class Buf(C.Structure): _fields_ = [('ptr', C.c_void_p), ('size', C.c_size_t), ('enc', C.c_uint)]
comp = C.c_void_p(); assert lib.DxcCreateInstance(CLSID, IID_C3, C.byref(comp)) == 0
def inline(path, seen):
    out = []
    for line in open(path, encoding='utf-8-sig'):
        m = re.match(r'\s*#include\s+"([^"]+)"', line)
        if m:
            p = os.path.join(os.path.dirname(path), m.group(1))
            if p not in seen: seen.add(p); out.append(inline(p, seen))
        else: out.append(line)
    return ''.join(out)
def blob(res, kind):
    b = C.c_void_p(); method(res, OFF + 4, C.c_long, C.c_uint, C.c_void_p, C.POINTER(C.c_void_p), C.c_void_p)(res, kind, IID_BLOB, C.byref(b), None)
    if not b.value: return b''
    p = method(b, OFF, C.c_void_p)(b); n = method(b, OFF + 1, C.c_size_t)(b); return C.string_at(p, n)
def compile(src_path, entry, profile, define=None):
    src = inline(src_path, set()).encode()
    args = ['-E', entry, '-T', profile, '-O3', '-Qstrip_debug', '-Qstrip_reflect'] + (['-D', define] if define else [])
    argv = (C.c_wchar_p * len(args))(*args); buf = Buf(C.cast(C.c_char_p(src), C.c_void_p), len(src), 65001)
    res = C.c_void_p()
    hr = method(comp, OFF, C.c_long, C.POINTER(Buf), C.c_void_p, C.c_uint, C.c_void_p, C.c_void_p, C.POINTER(C.c_void_p))(comp, C.byref(buf), argv, len(args), None, IID_RES, C.byref(res))
    status = C.c_long(); method(res, OFF, C.c_long, C.POINTER(C.c_long))(res, C.byref(status))
    return status.value, blob(res, 1), blob(res, 2).decode(errors='replace')
if __name__ == '__main__':
    shaders = out = os.path.join(ROOT, 'src', 'Mazesta.Diagnostics.Gpu', 'Shaders'); ps1 = os.path.join(ROOT, 'tools', 'compile-gpu-shaders.ps1'); only = sys.argv[1:]
    fails = 0
    for f, e, p, o, d in re.findall(r"@\('([^']+)', '([^']+)', '([^']+)', '([^']+)'(?:, '([^']+)')?\)", open(ps1).read()):
        if only and o not in only: continue
        st, obj, err = compile(os.path.join(shaders, f), e, p, d or None)
        if st != 0 or not obj: print('FAIL', o, err.strip()[:3000]); fails += 1; continue
        open(os.path.join(out, o), 'wb').write(obj); print('ok', o, len(obj), ('warn: ' + err.strip()[:500]) if err.strip() else '')
    sys.exit(fails)
