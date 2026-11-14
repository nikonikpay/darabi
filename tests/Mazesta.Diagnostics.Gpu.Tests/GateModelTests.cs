using Mazesta.Diagnostics.Gpu.Scene;
using Xunit;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GateModelTests
{
    // Reads the embedded scan: every triangle of it, stood 1.8 tall on y = 0 with unit normals and uv inside the picture.
    [Fact]
    public void The_embedded_gate_reads_whole_and_stands_where_it_should()
    {
        var g = GateModel.Load(); int nv = g.Vertices.Length / 9;
        Assert.True(nv > 250_000 && g.Indices.Length / 3 > 490_000 && g.Indices.Length % 3 == 0);
        Assert.All(g.Indices, i => Assert.True(i < nv));
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < nv; i += 997)
        {
            int a = i * 9; minY = Math.Min(minY, g.Vertices[a + 1]); maxY = Math.Max(maxY, g.Vertices[a + 1]);
            float len = MathF.Sqrt(g.Vertices[a + 3] * g.Vertices[a + 3] + g.Vertices[a + 4] * g.Vertices[a + 4] + g.Vertices[a + 5] * g.Vertices[a + 5]);
            Assert.InRange(len, 0.999f, 1.001f); Assert.InRange(g.Vertices[a + 6], 0f, 1f); Assert.InRange(g.Vertices[a + 7], 0f, 1f);
        }
        Assert.InRange(minY, -0.01f, 0.01f); Assert.InRange(maxY, 1.75f, 1.801f);
        Assert.Equal(4096, g.Size); Assert.True(g.Albedo.Length >= 11);
    }
}
