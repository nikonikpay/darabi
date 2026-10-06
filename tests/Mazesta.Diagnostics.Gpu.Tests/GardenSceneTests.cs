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

    [Fact] public void The_fountain_s_streams_rise_from_the_nozzle_and_fall_from_the_rim_to_the_pool()
    {
        var f = G.Fountain!; var jet = GardenGpu.JetColumn(f);
        Assert.Equal(f.Nozzle, jet[0].At);
        Assert.All(jet, p => { Assert.Equal(f.Nozzle.X, p.At.X); Assert.Equal(f.Nozzle.Z, p.At.Z); Assert.InRange(p.Radius, 0.01f, 0.05f); });
        Assert.InRange(jet[^1].At.Y - f.Nozzle.Y, 0.6f, 0.95f);           // not above where the slowest droplet turns (0.82 m)
        Assert.True(jet[^2].Radius > jet[0].Radius * 1.3f);                // thicker where it has slowed
        var spill = GardenGpu.SpillStream(f, G.WaterLevel);
        Assert.Equal(f.RimRadius, spill[0].At.X, 4); Assert.InRange(spill[0].At.Y, f.BowlLevel, f.BowlLevel + 0.05f);   // from the rim
        Assert.Equal(G.WaterLevel, spill[^1].At.Y, 3);                    // to the pool's surface, and no farther
        Assert.InRange(spill[^1].At.X - f.RimRadius, 0.1f, 0.35f);        // a hand's breadth out from under the rim
        for (int k = 1; k < spill.Length; k++) { Assert.True(spill[k].At.Y < spill[k - 1].At.Y && spill[k].At.X > spill[k - 1].At.X); Assert.True(spill[k].Radius <= spill[k - 1].Radius); }
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
            if (eye.Z > -1.8f) { inside = true; Assert.InRange(eye.X, -3.3f, 3.3f); Assert.InRange(eye.Y, 2.5f, 3.0f); }   // standing on the hall's floor (1.11), in the middle rooms: the tea corner (x -8.8) is looked at from across the hall, not stood over
            // the benches on the outer walks (x 9.12 to 9.84 either side, z -28.7 to -27.1 and -17.5 to -15.9): passed in front, clear of the inner bed (8.18)
            if (eye.Y < 2.2f && (eye.Z is > -28.9f and < -26.9f || eye.Z is > -17.7f and < -15.7f) && MathF.Abs(eye.X) > 8f) Assert.InRange(MathF.Abs(eye.X), 8.45f, 8.9f);
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
        Assert.True(Vector3.Distance(GardenDay.MoonAt(placed, 0), GardenDay.MoonAt(placed, GardenCamera.Loop)) < 1e-4f);
        float least = 1, most = 0;
        for (float t = 0; t < GardenCamera.Loop; t += 1f)
        {
            var m = GardenDay.MoonAt(placed, t); Assert.Equal(1, m.Length(), 4);
            least = MathF.Min(least, m.Y); most = MathF.Max(most, m.Y);
        }
        Assert.True(least > 0.3f, "the moon must stay well above the walls");   // 17 degrees and more
        Assert.True(Vector3.Dot(GardenDay.MoonAt(placed, GardenCamera.Loop / 4), GardenDay.MoonAt(placed, GardenCamera.Loop * 3 / 4)) < 0.85f);   // its shadows do move: over 30 degrees between its two ends
    }

    /// <summary>The embedded scene's own sun and moon (toward each, and its light), as <see cref="GardenGpu"/> takes them from its two scenes.</summary>
    private static GardenDay DayAt(float t)
    {
        var sun = G.Lights.Single(l => l.Kind == GardenLightKind.Sun && (l.Mask & 2) == 0); var moon = G.Lights.Single(l => l.Kind == GardenLightKind.Sun && (l.Mask & 2) != 0);
        return GardenDay.At(t, Vector3.Normalize(-sun.Direction), sun.Color * sun.Energy, Vector3.Normalize(-moon.Direction), moon.Color * moon.Energy);
    }

    [Fact] public void The_walk_goes_through_a_whole_day_and_ends_in_the_light_it_began_in()
    {
        Assert.Equal(DayAt(0), DayAt(GardenCamera.Loop)); Assert.Equal(DayAt(37.5f), DayAt(37.5f));
        float night = 0, lamps = 0; GardenDay before = DayAt(-0.25f);
        for (float t = 0; t < GardenCamera.Loop; t += 0.25f)
        {
            var d = DayAt(t);
            Assert.Equal(1, d.Key.Length(), 4); Assert.True(d.Key.Y > 0.02f, $"the key light is below the ground at {t}");
            Assert.InRange(d.Night, 0, 1); Assert.InRange(d.Lamps, 0, 1); Assert.InRange(d.Noon, 0, 1); Assert.InRange(d.Sun, 0, 1);
            // by day the sun is the key light and no lamp burns; in full night the moon is, and every lamp does
            if (d.Night == 0) { Assert.False(d.Moon); Assert.Equal(0, d.Lamps); Assert.True(d.KeyColor.X > 0); }
            if (d.Night == 1) { Assert.True(d.Moon); Assert.Equal(1, d.Lamps); Assert.True(d.KeyColor.Z > d.KeyColor.X); night += 0.25f; }
            if (d.Lamps == 1) lamps += 0.25f;
            // nothing jumps from one frame to the next: not the sky, not the lamps, and the key light only while it is dark (sun to moon)
            Assert.True(Vector3.Distance(d.Zenith, before.Zenith) < 0.02f && Vector3.Distance(d.Horizon, before.Horizon) < 0.03f && MathF.Abs(d.Lamps - before.Lamps) < 0.08f, $"t={t}");
            if (Vector3.Distance(d.Key, before.Key) > 0.03f) Assert.True(d.KeyColor.Length() < 0.05f && before.KeyColor.Length() < 0.05f, $"the key light jumps while it shines at {t}");
            before = d;
        }
        Assert.InRange(night, 20f, 50f); Assert.True(lamps >= night);   // a good part of the walk is by night, the lamps lit a little longer than it lasts
    }

    [Fact] public void While_the_walk_is_in_the_hall_the_sun_shines_in_at_its_windows_and_sinks()
    {
        // the hall's windows face down the garden (-z); the walk is inside from the threshold (48 s) to the door again (102 s)
        float highest = 0, lowest = 90, reachBefore = 0, first = 0;
        for (float t = 54; t <= 96; t += 6)
        {
            var d = DayAt(t); float up = MathF.Asin(d.Key.Y) * 180 / MathF.PI;
            Assert.False(d.Moon); Assert.True(d.Key.Z < -0.5f, $"the sun is not before the hall at {t}");   // within 60 degrees of straight in
            Assert.InRange(up, 5f, 41f); highest = MathF.Max(highest, up); lowest = MathF.Min(lowest, up);
            // how far from the sill the light of a window's top lies on the floor (the pane's top is 4 m over it): farther every key
            float reach = 4 / MathF.Tan(up * MathF.PI / 180);
            Assert.True(reach > reachBefore + 0.1f, $"the light does not creep on at {t}"); reachBefore = reach; if (first == 0) first = reach;
        }
        Assert.True(highest > 30 && lowest < 12 && reachBefore > 4 * first);
        // the scene's own sun, as the .blend has it, is a moment of that afternoon
        var placed = Vector3.Normalize(-G.Lights.Single(l => l.Kind == GardenLightKind.Sun && (l.Mask & 2) == 0).Direction);
        Assert.Contains(Enumerable.Range(0, 400).Select(k => DayAt(60 + k * 0.1f).Key), key => Vector3.Dot(key, placed) > 0.995f);
        // and it sets as the walk leaves: at the door the sun is on the horizon, on the stairs it is night
        Assert.InRange(DayAt(102).Key.Y, 0.02f, 0.1f); Assert.Equal(1, DayAt(120).Night);
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

    [Fact] public void A_lamp_s_six_views_see_all_round_it_each_down_its_own_axis()
    {
        var at = new Vector3(1, 2, 3); Vector3[] ways = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (int face = 0; face < 6; face++)
        {
            var c = Vector4.Transform(new Vector4(at + ways[face] * 4, 1), GardenRaster.LampFace(at, 10, face));
            Assert.Equal(0, c.X / c.W, 4); Assert.Equal(0, c.Y / c.W, 4); Assert.Equal(4, c.W, 4);   // straight ahead, four metres off
            // the depth the shader works out for that distance (GardenRaster.hlsl LampShadow) is the one the view stores
            Assert.Equal(10 / (10 - GardenGpu.LampNear) * (1 - GardenGpu.LampNear / 4), c.Z / c.W, 5);
            // a point off to the side is inside the view while it is nearer the axis than along it, and outside beyond
            var side = ways[(face + 2) % 6];
            var inside = Vector4.Transform(new Vector4(at + ways[face] * 4 + side * 3.9f, 1), GardenRaster.LampFace(at, 10, face));
            var outside = Vector4.Transform(new Vector4(at + ways[face] * 4 + side * 4.1f, 1), GardenRaster.LampFace(at, 10, face));
            Assert.True(MathF.Max(MathF.Abs(inside.X), MathF.Abs(inside.Y)) < inside.W && MathF.Max(MathF.Abs(outside.X), MathF.Abs(outside.Y)) > outside.W);
        }
        // the night's lamps (the ray-traced scene's rig, which both tests light at dusk): the ones over the water that are points get a
        // cube each as far as the cubes go - the lanterns', the hall's, the canopy's and the gate's among them
        Assert.True(G.Lights.Count(l => (l.Mask & 2) != 0 && l.Kind == GardenLightKind.Point && l.Position.Y > G.WaterLevel) >= GardenGpu.MostShadowLamps - 1);
    }

    [Fact] public void The_pictures_of_the_surroundings_are_taken_from_open_air_inside_the_space_each_stands_for()
    {
        Assert.Equal(2, GardenRaster.Rounds.Length);
        foreach (var (from, low, high) in GardenRaster.Rounds)
            Assert.True(from.X > low.X + 1 && from.Y > low.Y + 1 && from.Z > low.Z + 1 && from.X < high.X - 1 && from.Y < high.Y - 1 && from.Z < high.Z - 1);
        // the courtyard's: over the pool, clear of the fountain's bowl and jet and of the logo; the hall's: inside the hall, off its floor
        var (yard, _, _) = GardenRaster.Rounds[0]; var f = G.Fountain!;
        Assert.True(new Vector2(yard.X - f.Nozzle.X, yard.Z - f.Nozzle.Z).Length() > f.RimRadius + 2 && yard.Y > G.WaterLevel + 1);
        var (hall, hallLow, hallHigh) = GardenRaster.Rounds[1];
        Assert.True(hall.Z > -3 && hall.Y > 2 && hallLow.Z >= -3.3f && hallHigh.Z <= 7.2f);
        // the walk's eye inside the hall is inside the hall's box, outside it is not
        for (float t = 0; t < GardenCamera.Loop; t += 0.5f)
        {
            var (eye, _) = GardenCamera.At(t); bool inBox = eye.X > hallLow.X && eye.X < hallHigh.X && eye.Y > hallLow.Y && eye.Y < hallHigh.Y && eye.Z > hallLow.Z && eye.Z < hallHigh.Z;
            if (eye.Z > -2.5f) Assert.True(inBox, $"t={t}"); else if (eye.Z < -3.5f) Assert.False(inBox, $"t={t}");
        }
    }

    [Fact] public void The_garden_s_small_life_keeps_to_the_beds_and_is_the_same_at_the_same_moment()
    {
        for (float t = 0; t < GardenCamera.Loop; t += 2.3f)
        {
            for (int n = 0; n < GardenGpu.Fireflies; n++)
            {   // among the plants of the beds either side of the pool, inside the walls (x 13.5)
                var (at, glow) = GardenGpu.Firefly(n, t); Assert.InRange(glow, 0, 1);
                Assert.InRange(MathF.Abs(at.X), 2.9f, 13.2f); Assert.InRange(at.Z, -31f, -9f); Assert.InRange(at.Y, 0.1f, 2.8f);
            }
            for (int n = 0; n < GardenGpu.Butterflies; n++)
            {   // over the flowers of the beds by the pool, their wings never flat and never closed
                var (at, _, raised) = GardenGpu.Butterfly(n, t); Assert.InRange(raised, 0.1f, 1.25f);
                Assert.InRange(MathF.Abs(at.X), 2.1f, 9.7f); Assert.InRange(at.Z, -31f, -8.5f); Assert.InRange(at.Y, 0.4f, 1.9f);
            }
        }
        Assert.Equal(GardenGpu.Firefly(5, 77.7f), GardenGpu.Firefly(5, 77.7f)); Assert.Equal(GardenGpu.Butterfly(5, 77.7f), GardenGpu.Butterfly(5, 77.7f));
        Assert.True(Vector3.Distance(GardenGpu.Butterfly(3, 20).At, GardenGpu.Butterfly(3, 21).At) > 0.1f);   // they do fly
        Assert.True(GardenGpu.LitFireflies <= GardenGpu.Fireflies);
        // the steam rises from the tea counter, by the hall's left wall
        Assert.True(GardenGpu.KettleSpout.X < -8.5f && GardenGpu.KettleSpout.Y > 2 && GardenGpu.SamovarCrown.Y > GardenGpu.KettleSpout.Y);
    }

    [Fact] public void The_wind_bends_the_plants_where_they_stand_and_nothing_else()
    {
        var swaying = G.Instances.Where(i => GardenScene.Sway(i.Flags) > 0).ToList();
        Assert.True(swaying.Count > 5_000);                                                       // every tree, shrub, tuft and leaf
        Assert.All(swaying, i => Assert.Equal(0u, i.Flags & 7));                                  // not the logo, the sphere or a droplet
        Assert.All(swaying, i => Assert.InRange(GardenScene.Sway(i.Flags), 0.005f, 0.2f));
        Assert.True(G.Instances.Count(i => GardenScene.Sway(i.Flags) == 0) > 150);               // the building, the furniture, the rugs stand still
        // the wind is a gentle one, the same at the same moment, and never still for long
        float most = 0;
        for (float t = 0; t < 60; t += 0.1f) { var w = GardenGpu.Wind(new(3, 0, -12), t); most = MathF.Max(most, w.Length()); Assert.True(w.Length() < 2.2f); }
        Assert.True(most > 1.2f); Assert.Equal(GardenGpu.Wind(new(3, 0, -12), 7.5f), GardenGpu.Wind(new(3, 0, -12), 7.5f));
        Assert.True((GardenGpu.Wind(new(3, 0, -12), 7.5f) - GardenGpu.Wind(new(3, 0, -12), 8.5f)).Length() > 0.05f);
    }

    private sealed class Vec3Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
