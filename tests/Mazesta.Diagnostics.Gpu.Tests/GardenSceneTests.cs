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
        // the logo, once, in both scenes; the low sun only in Direct3D, the moon only in the ray-traced scene
        var logo = Assert.Single(G.Instances, i => (i.Flags & GardenScene.LogoFlag) != 0);
        Assert.Equal(3u, logo.Mask);
        Assert.Single(G.Lights, l => l.Kind == GardenLightKind.Sun && l.Mask == 1);
        Assert.Single(G.Lights, l => l.Kind == GardenLightKind.Sun && l.Mask == 2);
        Assert.True(G.Lights.Count(l => (l.Mask & 2) != 0 && l.Kind != GardenLightKind.Sun) > 30);   // the ray-traced scene's light rig
        Assert.Equal(2u, Assert.Single(G.Instances, i => (i.Flags & GardenScene.SphereFlag) != 0).Mask);   // its mirror sphere
        Assert.DoesNotContain(G.Instances, i => (i.Flags & GardenScene.DropletFlag) != 0);   // the fountain's water is the renderer's, not the file's
    }

    [Fact] public void The_pool_s_water_is_one_sheet_at_each_level()
    {
        // The .blend models water as slabs: an underside a centimetre or two under the surface was drawn through it, and the pool flickered.
        // The exporter keeps only what faces up or sideways, so no two level faces of water lie within a few centimetres of each other.
        var water = G.Materials.Select((m, i) => (m, i)).Where(x => x.m.Kind == GardenMaterialKind.Water).Select(x => (uint)x.i).ToHashSet();
        var levels = new List<float>();
        foreach (var mesh in G.Meshes)
            foreach (var sub in mesh.Submeshes.Where(s => water.Contains(s.Material)))
                for (uint k = sub.IndexStart; k < sub.IndexStart + sub.IndexCount; k += 3)
                {
                    Vector3 a = mesh.Position((int)mesh.Indices[k]), b = mesh.Position((int)mesh.Indices[k + 1]), c = mesh.Position((int)mesh.Indices[k + 2]);
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.Length() > 1e-9f && MathF.Abs(Vector3.Normalize(n).Y) > 0.99f) levels.Add(a.Y);
                }
        levels.Sort(); Assert.True(levels.Count > 10);
        for (int k = 1; k < levels.Count; k++)
        {
            float gap = levels[k] - levels[k - 1];
            Assert.True(gap < 0.004f || gap > 0.03f, $"water at {levels[k - 1]} and again at {levels[k]}");
        }
        Assert.Contains(levels, y => MathF.Abs(y - G.WaterLevel) < 0.004f);   // the main pool's
        Assert.True(levels[0] > G.WaterLevel - 0.004f);                       // and nothing under it
    }

    [Fact] public void The_fountain_stands_in_the_pool_and_every_droplet_stays_between_its_nozzle_and_the_water()
    {
        var f = G.Fountain; Assert.NotNull(f);
        Assert.InRange(f.Nozzle.X, -0.5f, 0.5f); Assert.InRange(f.Nozzle.Z, -20f, -18f);   // the middle of the pool
        Assert.True(f.Nozzle.Y > f.BowlLevel && f.BowlLevel > G.WaterLevel + 0.5f && f.RimRadius > f.BowlRadius);
        int flying = 0, splashed = 0, overRim = 0;
        for (uint id = 0; id < GardenGpu.JetDroplets + GardenGpu.SpillDroplets; id++)
            for (float t = 0; t < 5; t += 0.07f)
            {
                var (p, v, size) = GardenGpu.Droplet(f, G.WaterLevel, id, t);
                if (size <= 0) continue;
                flying++;
                Assert.InRange(p.Y, G.WaterLevel - 0.01f, f.Nozzle.Y + 1.5f);                       // never under the pool's surface, never higher than the jet throws
                float r = MathF.Sqrt((p.X - f.Nozzle.X) * (p.X - f.Nozzle.X) + (p.Z - f.Nozzle.Z) * (p.Z - f.Nozzle.Z));
                Assert.True(r < 3f, $"droplet {id} is {r} m from the fountain");                    // well inside the pool (3.1 m to its edge)
                if (r < f.RimRadius - 0.02f) Assert.True(p.Y >= f.BowlLevel - 0.01f, $"droplet {id} is inside the bowl's stone at {p.Y}");   // it met the bowl's water, and did not go through it
                if (r < f.RimRadius && v.Y > 0 && p.Y < f.Nozzle.Y - 0.2f && id < GardenGpu.JetDroplets) splashed++;   // rising again from the bowl
                if (r > f.RimRadius && p.Y < f.BowlLevel) overRim++;
            }
        Assert.True(flying > 10_000); Assert.True(splashed > 200, "no droplet splashed off the bowl"); Assert.True(overRim > 1_000, "nothing fell from the rim to the pool");
        // the same droplet at the same time is in the same place: a frame drawn twice is one picture
        Assert.Equal(GardenGpu.Droplet(f, G.WaterLevel, 17, 3.21f), GardenGpu.Droplet(f, G.WaterLevel, 17, 3.21f));
        // drawn out along its flight, by more the faster it falls
        var slow = GardenGpu.DropletWorld(new(0, 1, 0), new(0, -0.5f, 0), 0.02f); var fast = GardenGpu.DropletWorld(new(0, 1, 0), new(0, -4f, 0), 0.02f);
        Assert.True(fast.M22 > slow.M22 && slow.M22 > slow.M11); Assert.Equal(0.02f, fast.M11, 5);
    }

    [Fact] public void The_mirror_sphere_circles_the_fountain_over_the_water_clear_of_everything()
    {
        var sphere = G.Instances.Single(i => (i.Flags & GardenScene.SphereFlag) != 0); var mesh = G.Meshes[(int)sphere.Mesh];
        var start = sphere.Apply(mesh.Centre); float radius = mesh.Extent.X * sphere.Row0.X; var f = G.Fountain!;
        var logo = G.Instances.Single(i => (i.Flags & GardenScene.LogoFlag) != 0); var logoMesh = G.Meshes[(int)logo.Mesh];
        float logoUnder = logo.Apply(logoMesh.Centre).Y - logoMesh.Extent.Y * logo.Row1.Y - 0.121f;   // its lowest, at the bottom of its float
        Assert.Equal(Vector3.Zero, GardenGpu.SphereDrift(0)); Assert.True(GardenGpu.SphereDrift(GardenGpu.SphereLap).Length() < 1e-3f);
        for (float t = 0; t < GardenGpu.SphereLap; t += 0.2f)
        {
            var p = start + GardenGpu.SphereDrift(t);
            Assert.True(MathF.Abs(p.X) + radius < 3.1f && p.Z - radius > -26.1f && p.Z + radius < -11.9f, $"t={t}: off the pool at {p}");   // the pool: x ±3.1, z -26.1..-11.9
            Assert.True(p.Y - radius > G.WaterLevel + 0.3f && p.Y + radius < logoUnder, $"t={t}: height {p.Y}");
            Assert.True(new Vector2(p.X - f.Nozzle.X, p.Z - f.Nozzle.Z).Length() - radius > f.RimRadius + 0.5f, $"t={t}: in the fountain");
        }
        Assert.True((GardenGpu.SphereDrift(8) - GardenGpu.SphereDrift(24)).Length() > 8);   // it does travel: the two ends of the pool
    }


    [Fact] public void Every_material_kind_the_shaders_know_is_used_and_nothing_else()
    {
        var kinds = G.Materials.Select(m => m.Kind).ToHashSet();
        Assert.Contains(GardenMaterialKind.Water, kinds); Assert.Contains(GardenMaterialKind.Cutout, kinds); Assert.Contains(GardenMaterialKind.Glass, kinds);
        Assert.All(kinds, k => Assert.InRange((uint)k, 0u, 5u));
        Assert.All(G.Materials.Where(m => m.Kind == GardenMaterialKind.Cutout), m => Assert.True(m.Texture >= 0));
    }

    [Fact] public void Every_texture_is_a_whole_mip_chain_up_to_the_array_s_size_and_the_building_has_its_normal_maps()
    {
        Assert.Equal(1024, G.TextureSize);
        for (int t = 0; t < G.Textures.Count; t++)
        {
            var chain = G.Textures[t]; int own = 4 << (chain.Length - 1); Assert.InRange(own, 256, G.TextureSize);
            for (int m = 0, s = own; m < chain.Length; m++, s /= 2) Assert.Equal(s / 4 * (s / 4) * 16, chain[m].Length);   // down to one 4 x 4 block
            for (int m = 0, s = G.TextureSize; m < G.TextureMips; m++, s /= 2) Assert.Equal(s / 4 * (s / 4) * 16, G.TextureLevel(t, m).Length);   // and doubled up to the array's
        }
        Assert.Contains(G.Textures, t => t.Length == G.TextureMips);   // the stone, plaster, wood and tile are stored at the full size
        var relief = G.Materials.Where(m => m.NormalTexture >= 0).ToList();
        Assert.True(relief.Count >= 12);   // limestone, kahgel, walnut, the turquoise tile, the pool's mosaic, the gate, the furniture's wood and cloth
        Assert.All(relief, m => { Assert.Equal(GardenMaterialKind.Flat, m.Kind); Assert.True(m.Texture >= 0 && m.Texture != m.NormalTexture); });
        // stone that has no coordinates of its own is laid by world position; nothing else is
        Assert.All(G.Materials.Where(m => m.Pattern.Z > 0 && m.Kind == GardenMaterialKind.Flat), m => Assert.True(m.Texture >= 0 && m.NormalTexture >= 0));
        // only the orsi's coloured panes colour the light that passes through them
        var stained = G.Materials.Where(m => m.LightTint > 0).ToList();
        Assert.InRange(stained.Count, 3, 6); Assert.All(stained, m => Assert.Equal(GardenMaterialKind.Glass, m.Kind));
    }

    [Fact] public void A_texture_doubled_in_its_blocks_shows_each_texel_four_times()
    {
        // one block: end alphas 200 and 40, end colours white and black, texel k picks alpha k % 8 and colour k % 4
        ulong alpha = 0; uint color = 0; for (int k = 0; k < 16; k++) { alpha |= (ulong)(k % 8) << k * 3; color |= (uint)(k % 4) << k * 2; }
        byte[] block = [200, 40, (byte)alpha, (byte)(alpha >> 8), (byte)(alpha >> 16), (byte)(alpha >> 24), (byte)(alpha >> 32), (byte)(alpha >> 40), 0xFF, 0xFF, 0, 0, (byte)color, (byte)(color >> 8), (byte)(color >> 16), (byte)(color >> 24)];
        byte[] doubled = GardenScene.Bc3Doubled(block, 1);
        Assert.Equal(64, doubled.Length);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var b = doubled.AsSpan((y / 4 * 2 + x / 4) * 16, 16); int k = y % 4 * 4 + x % 4, from = y / 2 * 4 + x / 2;
                Assert.Equal(block.AsSpan(0, 2).ToArray(), b[..2].ToArray()); Assert.Equal(block.AsSpan(8, 4).ToArray(), b.Slice(8, 4).ToArray());
                Assert.Equal(from % 8, (int)(BitConverter.ToUInt64(b) >> 16 >> k * 3 & 7)); Assert.Equal(from % 4, (int)(BitConverter.ToUInt32(b[12..]) >> k * 2 & 3));
            }
    }

    [Fact] public void The_mountains_stand_all_the_way_round_the_horizon()
    {
        var sky = G.Backdrop; Assert.NotNull(sky);
        Assert.True(sky.Width >= 1024 && sky.Height >= 64); Assert.Equal(sky.Width / 4 * (sky.Height / 4) * 16, sky.Bc3.Length);
        Assert.Equal(0f, sky.TanLow); Assert.InRange(sky.TanHigh, 0.2f, 0.6f);   // from the horizon to some 20 degrees up
    }

    [Fact] public void The_logo_floats_over_the_pool_between_the_fountain_and_the_stairs()
    {
        var logo = G.Instances.Single(i => (i.Flags & GardenScene.LogoFlag) != 0); var mesh = G.Meshes[(int)logo.Mesh];
        var centre = logo.Apply(mesh.Centre);
        Assert.InRange(centre.X, -0.5f, 0.5f); Assert.InRange(centre.Z, -17f, -13f);   // the pool runs z -26..-12; the fountain has its middle, -19
        Assert.True(centre.Z > G.Fountain!.Nozzle.Z + G.Fountain.RimRadius + 1);
        Assert.InRange(centre.Y, 2.5f, 3.5f);                                           // over the heads of people, and over the fountain's jet
        Assert.InRange(mesh.Extent.X * 2 * logo.Row0.X, 3.2f, 3.8f);                    // three and a half metres across (it was five)
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
        Assert.Throws<InvalidDataException>(() => GardenScene.Read(Gz([.. "MZSC"u8.ToArray(), 3, 0, 0, 0])));         // the version before this one
        Assert.Throws<InvalidDataException>(() => GardenScene.Read(Gz([.. "MZSC"u8.ToArray(), 4, 0, 0, 0, 1, 0])));  // cut short
    }

    [Fact] public void The_camera_walks_the_courtyard_and_the_hall_and_comes_back_to_where_it_started()
    {
        bool inside = false;
        for (float t = 0; t < GardenCamera.Loop; t += 0.125f)
        {
            var (eye, target) = GardenCamera.At(t);
            Assert.InRange(eye.X, -13f, 13f); Assert.InRange(eye.Z, -33f, 5f); Assert.InRange(eye.Y, 1.5f, 3.2f);   // walls at x ±13.8 and z -33.3; the hall's back wall at 7
            Assert.True(Vector3.Distance(eye, target) > 3);
            // the hall's front wall is at z -3.2 to -3.0 and its door leaves stand open to -1.9: the walk passes only between them
            if (eye.Z > -3.4f && eye.Z < -1.8f) Assert.InRange(eye.X, -0.45f, 0.45f);
            if (eye.Z > -1.8f) { inside = true; Assert.InRange(eye.X, -8f, 8f); Assert.InRange(eye.Y, 2.5f, 3.0f); }   // standing on the hall's floor (1.11)
        }
        Assert.True(inside);
        var (start, _) = GardenCamera.At(0); var (end, _) = GardenCamera.At(GardenCamera.Loop);
        Assert.Equal(start, end);
        Assert.Equal(G.RasterCamera.Eye, GardenCamera.KeyFrames[0].Eye, new Vec3Near(0.01f));   // the walk starts at the .blend's own camera
    }

    [Fact] public void The_logo_turns_to_face_the_camera_wherever_the_walk_is()
    {
        var pivot = new Vector3(0, 2.9f, -15.3f);
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

    [Fact] public void The_moon_swings_across_the_sky_and_is_back_where_it_was_when_the_walk_ends()
    {
        var placed = Vector3.Normalize(new Vector3(0.62f, 0.52f, -0.42f));
        Assert.True(Vector3.Distance(GardenFrame.MoonAt(placed, 0), GardenFrame.MoonAt(placed, GardenCamera.Loop)) < 1e-4f);
        float least = 1, most = 0;
        for (float t = 0; t < GardenCamera.Loop; t += 1f)
        {
            var m = GardenFrame.MoonAt(placed, t); Assert.Equal(1, m.Length(), 4);
            least = MathF.Min(least, m.Y); most = MathF.Max(most, m.Y);
        }
        Assert.True(least > 0.3f, "the moon must stay well above the walls");   // 17 degrees and more
        Assert.True(Vector3.Dot(GardenFrame.MoonAt(placed, GardenCamera.Loop / 4), GardenFrame.MoonAt(placed, GardenCamera.Loop * 3 / 4)) < 0.85f);   // its shadows do move: over 30 degrees between its two ends
    }

    [Fact] public void The_frame_s_depth_is_reversed_one_at_the_near_plane_and_never_negative()
    {
        // the projection GardenFrame builds: depth = Near / distance
        float h = 1 / MathF.Tan(0.5f); var proj = new Matrix4x4(h / 1.5f, 0, 0, 0, 0, h, 0, 0, 0, 0, 0, 1, 0, 0, GardenFrame.Near, 0);
        float Depth(float z) { var c = Vector4.Transform(new Vector4(0, 0, z, 1), proj); return c.Z / c.W; }
        Assert.Equal(1f, Depth(GardenFrame.Near), 5); Assert.True(Depth(30) > Depth(30.01f) && Depth(1e6f) > 0);
        // two surfaces a centimetre apart thirty metres away are thousands of depth steps apart (they were a handful: the pool flickered)
        Assert.True((Depth(30) - Depth(30.01f)) / (BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(Depth(30)) + 1) - Depth(30)) > 2000);
    }

    [Fact] public void The_sun_s_shadow_view_holds_the_whole_courtyard()
    {
        var vp = GardenRaster.SunShadowMatrix(Vector3.Normalize(new Vector3(-0.7f, 0.4f, -0.5f)));
        foreach (var corner in new[] { new Vector3(-14, 0, -34), new Vector3(14, 0, 7), new Vector3(-14, 11, 7), new Vector3(14, 11, -34) })
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
