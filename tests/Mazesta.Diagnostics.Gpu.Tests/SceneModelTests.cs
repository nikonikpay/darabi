using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class SceneModelTests
{
    [Fact] public void The_logo_is_a_solid_centred_to_the_standard_width()
    {
        var m = SceneModel.Logo();
        Assert.True(m.Triangles > 500); Assert.Equal(0, m.Vertices.Length % 3);
        float minX = m.Vertices.Min(v => v.Position.X), maxX = m.Vertices.Max(v => v.Position.X);
        Assert.Equal(SceneModel.Width, maxX - minX, 3); Assert.Equal(0, (minX + maxX) / 2, 3);
        Assert.All(m.Vertices, v => Assert.Equal(1, v.Normal.Length(), 3));
    }

    [Fact] public void Ear_clipping_covers_a_concave_outline_exactly()
    {
        List<Vector2> l = [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 3), new(0, 3)];   // an L, area 6
        float area = SceneModel.EarClip(l).Sum(t => SceneModel.SignedArea([l[t.Item1], l[t.Item2], l[t.Item3]]));
        Assert.Equal(6, area, 4); Assert.Equal(4, SceneModel.EarClip(l).Count);
    }

    [Fact] public void An_obj_model_replaces_the_logo_with_flat_normals_and_the_same_size()
    {
        var m = SceneModel.FromObj("v 0 0 0\nv 10 0 0\nv 10 10 0\nv 0 10 0\nf 1 2 3 4\nf -4/1/1 -2/2/2 -1/3/3\n", "quad.obj");
        Assert.Equal(3, m.Triangles);
        Assert.Equal(SceneModel.Width, m.Vertices.Max(v => v.Position.X) - m.Vertices.Min(v => v.Position.X), 3);
        Assert.All(m.Vertices, v => Assert.Equal(Vector3.UnitZ, v.Normal));
        Assert.Throws<InvalidDataException>(() => SceneModel.FromObj("v 0 0 0\nf 1 2 3\n", "bad.obj"));
    }

    [Fact] public void Fur_normals_are_shared_where_faces_meet_so_the_layers_stay_closed()
    {
        var m = SceneModel.Logo(); var smooth = m.SmoothNormals();
        Assert.Equal(m.Vertices.Select(v => v.Position), smooth.Select(v => v.Position));
        Assert.All(smooth, v => Assert.Equal(1, v.Normal.Length(), 3));
        // Every copy of one point now has one normal, where the flat model has one per face.
        Assert.All(smooth.GroupBy(v => v.Position), g => Assert.Single(g.Select(v => v.Normal).Distinct()));
        Assert.Contains(m.Vertices.GroupBy(v => v.Position), g => g.Select(v => v.Normal).Distinct().Count() > 1);
    }

    [Fact] public void The_ray_traced_placement_matches_the_shader_s_centre_model()
    {
        var m = GpuSceneExecutor.Placement(0, 0);   // at time 0 the centre model is unrotated, lifted 0.3
        Assert.Equal(Vector3.UnitX, new Vector3(m.M11, m.M21, m.M31)); Assert.Equal(0.3f, m.M24, 5);
    }
}
