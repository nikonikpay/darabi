using System.Numerics; using Xunit; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;
public class GardenLodTests
{
    private static bool Cutout(GardenScene s, uint m) => s.Materials[(int)m].Kind == GardenMaterialKind.Cutout;

    [Fact]
    public void Each_level_has_fewer_triangles_and_leaves_the_solid_parts_whole()
    {
        var scene = GardenScene.Embedded; int made = 0;
        foreach (var mesh in scene.Meshes)
        {
            if (GardenLod.Build(mesh, m => Cutout(scene, m)) is not { } lod) continue;
            made++; long before = mesh.Indices.Length;
            foreach (var level in lod.Levels)
            {
                Assert.True(level.Indices.Length < before, "a level is coarser than the one before"); before = level.Indices.Length;
                Assert.All(level.Indices, i => Assert.True(i < level.VertexCount));
                Assert.Equal(mesh.Submeshes.Length, level.Submeshes.Length);
                for (int s = 0; s < mesh.Submeshes.Length; s++)
                    if (!Cutout(scene, mesh.Submeshes[s].Material)) Assert.Equal(mesh.Submeshes[s].IndexCount, level.Submeshes[s].IndexCount);
            }
        }
        Assert.True(made > 5, "the garden's trees and shrubs have levels");
    }

    [Fact]
    public void Chunks_cover_a_submesh_once_and_keep_its_triangles()
    {
        var scene = GardenScene.Embedded; int cut = 0;
        foreach (var mesh in scene.Meshes)
            if (GardenChunks.Split(mesh, m => !Cutout(scene, m) && scene.Materials[(int)m].Kind is GardenMaterialKind.Flat or GardenMaterialKind.Brick) is { } split)
                foreach (var (si, chunks) in split.Chunks)
                {
                    cut++; var sub = mesh.Submeshes[si];
                    Assert.Equal(sub.IndexCount, (uint)chunks.Sum(c => c.Count)); Assert.Equal(0u, chunks[0].Start);
                    for (int k = 1; k < chunks.Length; k++) Assert.Equal(chunks[k - 1].Start + chunks[k - 1].Count, chunks[k].Start);
                    string Triangles(uint[] idx) => string.Join(",", Enumerable.Range(0, (int)sub.IndexCount / 3).Select(t => string.Join(" ", idx.Skip((int)sub.IndexStart + t * 3).Take(3).Order())).Order());
                    Assert.Equal(Triangles(mesh.Indices), Triangles(split.Mesh.Indices));
                    foreach (var c in chunks)   // every vertex of a run lies in its ball
                        for (uint k = c.Start; k < c.Start + c.Count; k++) Assert.True(Vector3.Distance(split.Mesh.Position((int)split.Mesh.Indices[sub.IndexStart + k]), c.Centre) <= c.Radius);
                }
        Assert.True(cut > 0, "the garden has big solid meshes");
    }
}
