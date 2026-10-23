#:project ../../../src/Mazesta.Diagnostics.Gpu/Mazesta.Diagnostics.Gpu.csproj
#:property TargetFramework=net10.0-windows
// Writes the app's own Persian garden (meshes, placements, materials, small BC3 textures, camera, pool) to ../garden.js, so the weather
// test page draws and collides with the very scene the benchmark uses. Run: dotnet run export-garden.cs
using System.IO.Compression; using System.Numerics; using System.Runtime.InteropServices; using System.Text; using Mazesta.Diagnostics.Gpu.Scene;

const int TexMax = 128; // texels across: enough for leaf cut-outs and a colour, small enough for a page
var s = GardenScene.Embedded;
const uint Skip = GardenScene.LogoFlag | GardenScene.SphereFlag | GardenScene.DropletFlag | GardenScene.MoverFlag | GardenScene.WeatherFlag;
var inst = s.Instances.Where(i => (i.Mask & (uint)GardenScene.Mode.Raster) != 0 && (i.Flags & Skip) == 0).ToArray();
var used = inst.Select(i => (int)i.Mesh).Distinct().OrderBy(m => m).ToArray(); var remap = used.Select((m, k) => (m, k)).ToDictionary(p => p.m, p => p.k);

using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
void V3(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
// textures: the chain from TexMax (or the texture's own size) down to 4
w.Write(s.Textures.Count);
foreach (var chain in s.Textures)
{
    int own = 4 << (chain.Length - 1), first = own > TexMax ? int.Log2(own / TexMax) : 0;
    w.Write(Math.Min(own, TexMax)); w.Write(chain.Length - first);
    for (int k = first; k < chain.Length; k++) { w.Write(chain[k].Length); w.Write(chain[k]); }
}
w.Write(s.Materials.Length);
w.Write(MemoryMarshal.AsBytes(s.Materials.AsSpan())); // 96 bytes each, GardenMaterial's layout
long tris = 0;
w.Write(used.Length);
foreach (int mi in used)
{
    var mesh = s.Meshes[mi];
    w.Write(mesh.VertexCount); w.Write(mesh.Indices.Length); w.Write(mesh.Submeshes.Length); V3(mesh.Centre); V3(mesh.Extent);
    foreach (var sub in mesh.Submeshes) { w.Write(sub.IndexStart); w.Write(sub.IndexCount); w.Write(sub.Material); }
    w.Write(mesh.Vertices); // snorm16x4 position, snorm8x4 normal, float16x2 uv
    w.Write(MemoryMarshal.AsBytes(mesh.Indices.AsSpan())); tris += mesh.Indices.Length / 3;
}
w.Write(inst.Length);
foreach (var i in inst) { w.Write(remap[(int)i.Mesh]); w.Write(i.Flags); foreach (var r in new[] { i.Row0, i.Row1, i.Row2 }) { w.Write(r.X); w.Write(r.Y); w.Write(r.Z); w.Write(r.W); } }
w.Flush();

var (eye, target, fov) = s.RasterCamera; var f = s.Fountain;
var sun = s.Lights.FirstOrDefault(l => l.Kind == GardenLightKind.Sun);
string J(Vector3 v) => FormattableString.Invariant($"[{v.X},{v.Y},{v.Z}]");
var meta = FormattableString.Invariant($"{{eye:{J(eye)},target:{J(target)},fov:{fov},water:{s.WaterLevel},sun:{J(sun.Direction)},fountain:{(f is null ? "null" : $"{{nozzle:{J(f.Nozzle)},bowl:{f.BowlRadius},level:{f.BowlLevel},rim:{f.RimRadius}}}")}}}");
var outPath = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "garden.js"));
File.WriteAllText(outPath, $"window.GARDEN={meta};window.GARDEN.data=\"{Convert.ToBase64String(Gzip(ms))}\";\n", Encoding.ASCII);
Console.WriteLine($"{s.Textures.Count} textures, {s.Materials.Length} materials, {used.Length} meshes, {inst.Length} placements, {tris:N0} unique triangles, {ms.Length / 1e6:F1} MB raw -> {outPath}");

static byte[] Gzip(MemoryStream m) { using var o = new MemoryStream(); using (var z = new GZipStream(o, CompressionLevel.SmallestSize)) z.Write(m.GetBuffer(), 0, (int)m.Length); return o.ToArray(); }
