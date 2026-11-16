using System.Numerics; using Xunit; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;
/// <summary>Not a test: with MAZESTA_BAKE_LOGO set to a file path it writes the garden's gilt logo as Scene/logo.mzlogo (the temperature test's turf wears it): int vertices, int indices,
/// then x y z nx ny nz floats a vertex (width 1 along x, centred, facing +z, y up) and the uint indices. Without it the test does nothing.</summary>
public class LogoBakeTests
{
    [Fact]
    public void Bake_the_logo_when_asked()
    {
        if (Environment.GetEnvironmentVariable("MAZESTA_BAKE_LOGO") is not { Length: > 0 } path) return;
        var scene = GardenScene.Embedded; var inst = scene.Instances.First(i => (i.Flags & GardenScene.LogoFlag) != 0); var mesh = scene.Meshes[(int)inst.Mesh];
        int n = mesh.VertexCount; var p = new Vector3[n]; var nr = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            p[i] = inst.Apply(mesh.Position(i));
            var raw = mesh.Vertices.AsSpan(i * 16 + 8, 3); var v = new Vector3((sbyte)raw[0], (sbyte)raw[1], (sbyte)raw[2]) / 127f;
            var w = new Vector3(Vector3.Dot(new(inst.Row0.X, inst.Row0.Y, inst.Row0.Z), v), Vector3.Dot(new(inst.Row1.X, inst.Row1.Y, inst.Row1.Z), v), Vector3.Dot(new(inst.Row2.X, inst.Row2.Y, inst.Row2.Z), v));
            nr[i] = w.LengthSquared() > 1e-12f ? Vector3.Normalize(w) : Vector3.UnitY;
        }
        Vector3 lo = p[0], hi = p[0]; foreach (var q in p) { lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
        var e = hi - lo; var c = (lo + hi) / 2; float[] ex = [e.X, e.Y, e.Z];
        int t = Array.IndexOf(ex, ex.Min()), wd = Array.IndexOf(ex, ex.Max()), h = 3 - t - wd;
        float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
        bool odd = (wd, h, t) is (0, 2, 1) or (1, 0, 2) or (2, 1, 0);   // an odd permutation of the axes would mirror the letters
        using var o = new BinaryWriter(File.Create(path)); o.Write(n); o.Write(mesh.Indices.Length);
        for (int i = 0; i < n; i++)
        {
            float s = 1 / Axis(e, wd), z = (Axis(p[i], t) - Axis(c, t)) * s, nz = Axis(nr[i], t); if (odd) { z = -z; nz = -nz; }
            o.Write((Axis(p[i], wd) - Axis(c, wd)) * s); o.Write((Axis(p[i], h) - Axis(c, h)) * s); o.Write(z); o.Write(Axis(nr[i], wd)); o.Write(Axis(nr[i], h)); o.Write(nz);
        }
        foreach (var i in mesh.Indices) o.Write(i);
    }
}
