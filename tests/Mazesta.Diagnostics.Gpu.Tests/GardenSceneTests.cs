using System.IO.Compression; using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GardenSceneTests
{
    private static GardenScene G => GardenScene.Embedded;

    [Fact] public void The_embedded_garden_reads_whole_with_both_scenes_in_it()
    {
        Assert.True(G.Meshes.Count > 100); Assert.True(G.Textures.Count > 5); Assert.True(G.Materials.Length > 10);
        Assert.True(G.Triangles(GardenScene.Mode.Raster) > 1_000_000); Assert.True(G.Triangles(GardenScene.Mode.RayTraced) > 1_000_000);
        // the logo, once, in both scenes; the golden-hour sun only in Direct3D, the moon only in the ray-traced scene
        var logo = Assert.Single(G.Instances, i => (i.Flags & GardenScene.LogoFlag) != 0);
        Assert.Equal(3u, logo.Mask);
        Assert.Single(G.Lights, l => l.Kind == GardenLightKind.Sun && l.Mask == 1);
        Assert.Single(G.Lights, l => l.Kind == GardenLightKind.Sun && l.Mask == 2);
        Assert.True(G.Lights.Count(l => (l.Mask & 2) != 0 && l.Kind != GardenLightKind.Sun) > 30);   // the ray-traced scene's light rig
        Assert.Contains(G.Instances, i => i.Mask == 2);   // its glass orbs and mirror sphere
    }

    [Fact] public void Every_material_kind_the_shaders_know_is_used_and_nothing_else()
    {
        var kinds = G.Materials.Select(m => m.Kind).ToHashSet();
        Assert.Contains(GardenMaterialKind.Brick, kinds); Assert.Contains(GardenMaterialKind.Water, kinds); Assert.Contains(GardenMaterialKind.Cutout, kinds); Assert.Contains(GardenMaterialKind.Glass, kinds);
        Assert.All(kinds, k => Assert.InRange((uint)k, 0u, 5u));
        Assert.All(G.Materials.Where(m => m.Kind == GardenMaterialKind.Cutout), m => Assert.True(m.Texture >= 0));
    }

    [Fact] public void The_logo_floats_over_the_middle_of_the_pool()
    {
        var logo = G.Instances.Single(i => (i.Flags & GardenScene.LogoFlag) != 0); var mesh = G.Meshes[(int)logo.Mesh];
        var centre = logo.Apply(mesh.Centre);
        Assert.InRange(centre.X, -0.5f, 0.5f); Assert.InRange(centre.Z, -4f, -2f);   // the pool runs z -12..6, its middle -3
        Assert.InRange(centre.Y, 2f, 5f);
        Assert.InRange(mesh.Extent.X * 2, 4.5f, 5.5f);   // five metres across
    }

    [Fact] public void Quantised_positions_come_back_within_a_millimetre_of_the_mesh_bounds()
    {
        foreach (var m in G.Meshes.Take(60))
            for (int i = 0; i < m.VertexCount; i += 7)
            {
                var p = m.Position(i);
                Assert.True(Vector3.Abs(p - m.Centre).X <= m.Extent.X * 1.0001f + 1e-3f && Vector3.Abs(p - m.Centre).Y <= m.Extent.Y * 1.0001f + 1e-3f);
            }
    }

    [Fact] public void A_damaged_or_foreign_file_is_refused_with_a_reason()
    {
        static MemoryStream Gz(byte[] raw) { var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(raw); ms.Position = 0; return ms; }
        Assert.Throws<InvalidDataException>(() => GardenScene.Read(Gz("NOPE"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => GardenScene.Read(Gz([.. "MZSC"u8.ToArray(), 99, 0, 0, 0])));        // another version
        Assert.Throws<InvalidDataException>(() => GardenScene.Read(Gz([.. "MZSC"u8.ToArray(), 1, 0, 0, 0, 1, 0])));  // cut short
    }

    [Fact] public void The_camera_walks_inside_the_courtyard_and_comes_back_to_where_it_started()
    {
        for (float t = 0; t < GardenCamera.Loop; t += 0.25f)
        {
            var (eye, target) = GardenCamera.At(t);
            Assert.InRange(eye.X, -13f, 13f); Assert.InRange(eye.Z, -23f, 21f); Assert.InRange(eye.Y, 1.5f, 5f);
            Assert.True(Vector3.Distance(eye, target) > 3);
        }
        var (start, _) = GardenCamera.At(0); var (end, _) = GardenCamera.At(GardenCamera.Loop);
        Assert.Equal(start, end);
        Assert.Equal(G.RasterCamera.Eye, GardenCamera.KeyFrames[0].Eye, new Vec3Near(0.01f));   // the walk starts at the .blend's own camera
    }

    [Fact] public void The_logo_turns_to_face_the_camera_wherever_the_walk_is()
    {
        var pivot = new Vector3(0, 3.1f, -3);
        for (float t = 0; t < GardenCamera.Loop; t += 1.5f)
        {
            var (c, s, lift) = GardenGpu.LogoTurn(pivot, t);
            Assert.Equal(1, c * c + s * s, 4); Assert.InRange(lift, -0.121f, 0.121f);
            // the face (0, 0, -1), turned as the shaders turn it, points at the eye
            var face = Vector3.Transform(-Vector3.UnitZ, GardenGpu.LogoMotion(c, s, 0));
            var (eye, _) = GardenCamera.At(t); var toEye = Vector3.Normalize(new Vector3(eye.X - pivot.X, 0, eye.Z - pivot.Z));
            Assert.True(Vector3.Dot(face, toEye) > 0.999f, $"t={t}");
        }
        // at the start the camera is down the garden, so the logo stands nearly as it was placed
        Assert.True(GardenGpu.LogoTurn(pivot, 0).Cos > 0.97f);
    }

    [Fact] public void The_sun_s_shadow_view_holds_the_whole_courtyard()
    {
        var vp = GardenRaster.SunShadowMatrix(Vector3.Normalize(new Vector3(-0.7f, 0.4f, -0.5f)));
        foreach (var corner in new[] { new Vector3(-14, 0, -24), new Vector3(14, 0, 22), new Vector3(-14, 11, 22), new Vector3(14, 11, -24) })
        {
            var c = Vector4.Transform(new Vector4(corner, 1), vp);
            Assert.InRange(c.X, -1f, 1f); Assert.InRange(c.Y, -1f, 1f); Assert.InRange(c.Z, 0f, 1f);
        }
    }

    private sealed class Vec3Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
