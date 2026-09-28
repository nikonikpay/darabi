using System.Globalization; using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>One vertex of the test model: position and normal, the layout the scene shaders read from a structured buffer.</summary>
[StructLayout(LayoutKind.Sequential)] public readonly record struct SceneVertex(Vector3 Position, Vector3 Normal);

/// <summary>
/// A model as a plain triangle list (three vertices a triangle, no index buffer), centred on the origin and scaled to <see cref="Width"/>
/// units across. The Mazesta logo, extruded from the same SVG paths the app and the reports draw; or a Wavefront .obj at
/// <see cref="CustomPath"/>, which the visual GPU tests float over the garden's pool in the logo's place, so the owner can drop in their own
/// 3-D models without a new build.
/// </summary>
public sealed class SceneModel(string name, SceneVertex[] vertices)
{
    public const float Width = 4f;
    public string Name { get; } = name;
    public SceneVertex[] Vertices { get; } = vertices;
    public int Triangles => Vertices.Length / 3;

    /// <summary>Models\gpu-test.obj next to the app.</summary>
    public static string CustomPath => Path.Combine(AppContext.BaseDirectory, "Models", "gpu-test.obj");

    /// <summary>The owner's model when there is a readable one (it takes the logo's place in the garden), otherwise null;
    /// <paramref name="problem"/> says why a model that is there was not used.</summary>
    public static SceneModel? LoadCustom(out string? problem)
    {
        problem = null;
        if (!File.Exists(CustomPath)) return null;
        try { return FromObj(File.ReadAllText(CustomPath), Path.GetFileName(CustomPath)); }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException) { problem = $"{CustomPath} was not used: {e.Message}"; return null; }
    }

    /// <summary>The Mazesta logo, extruded to a quarter of its height.</summary>
    public static SceneModel Logo() => Extrude("Mazesta logo", LogoPaths.Select(SvgPath.Flatten), depthRatio: 0.2f);

    // The owner's mazesta-logo-fill.svg paths (the same ones Mazesta.Reporting.MazestaLogo and the web edition's logo.js draw).
    internal static readonly string[] LogoPaths =
    [
        "M1758.64 65.98l14.62 -0.06 169.15 0 16.19 0.13c75.44,0.59 136.62,62.03 136.89,137.47l0.78 221.07c0.14,38.09 -13.37,70.93 -40.26,97.9 -26.9,26.96 -59.72,40.57 -97.78,40.55l-109.25 -0.05 0.25 -40.76 0.09 -20.67 -1.46 -79.46c-0.21,-11.43 3.79,-21.35 11.88,-29.43 8.08,-8.09 17.99,-12.08 29.42,-11.89 17.51,0.31 34.21,0.49 36.92,-2.22 7.9,-7.93 11.87,-17.58 11.83,-28.79l-0.28 -79.75c-0.08,-22.35 -18.36,-40.51 -40.72,-40.44l-74.82 0.26c-22.2,0.07 -40.36,18.1 -40.44,40.31l-1.04 293.89 -137.03 -0.18 -26.36 0.18 -93.74 -0.93 -10.42 0.17c-38.3,0.6 -71.48,-12.66 -98.82,-39.49 -27.34,-26.82 -41.21,-59.75 -41.33,-98.06l-1.24 -425.35 168.13 -0.38 -1.97 350.24c-0.12,22.23 17.8,40.5 40.04,40.79l1.88 0.03c11.26,0.15 20.99,-3.76 29.01,-11.66 8.02,-7.89 12.08,-17.56 12.11,-28.82l0.35 -147.06c0.19,-75.66 61.77,-137.25 137.42,-137.55z",
        "M1117.76 562.98l-661.11 0.12 -50.79 -50.73 -58.12 50.6 -167.88 0 -47.28 -0.18c-75.8,-0.29 -132.58,-62.16 -132.58,-137.96l0 -424.83 167.77 0 -0.78 349.83c-0.02,11.2 3.95,20.83 11.86,28.76 7.9,7.92 17.53,11.92 28.72,11.92l69.46 0c22.33,0 40.54,-18.22 40.58,-40.55l0.03 -21.52c0.04,-22.32 18.25,-40.51 40.58,-40.51l132.17 0 0.09 62.1c0.03,22.32 18.24,40.55 40.58,40.55l546.14 0.02c22.34,0 40.58,18.24 40.58,40.58l0 131.81z",
        "M1322.48 564.04l0 -390 -152.17 0 0 348.48c0,22.34 18.23,40.58 40.58,40.58l71.02 0 40.58 0.94z",
        "M364.39 136l0 0 67.96 0 58.02 -0.05 0 -77.93c0,-31.95 -26.06,-58.02 -58.02,-58.02l-67.96 0 -1.13 0 -130.95 0 -0.05 58.02 0 77.57 69.39 0.4 62.74 0z",
        "M1322.48 133.78l-152.17 0 0 -68.85c0,-35.76 29.16,-64.92 64.92,-64.92l87.25 0 0 133.78z",
    ];

    /// <summary>Closed outlines (SVG units, y down) made solid: front and back faces by ear clipping, and a wall along every edge.</summary>
    internal static SceneModel Extrude(string name, IEnumerable<List<Vector2>> outlines, float depthRatio)
    {
        var polygons = outlines.Select(Clean).Where(p => p.Count >= 3).ToList();
        if (polygons.Count == 0) throw new InvalidDataException("The outline has no area.");
        var all = polygons.SelectMany(p => p).ToList();
        Vector2 min = new(all.Min(p => p.X), all.Min(p => p.Y)), max = new(all.Max(p => p.X), all.Max(p => p.Y));
        float scale = Width / (max.X - min.X), depth = (max.Y - min.Y) * scale * depthRatio;
        Vector2 center = (min + max) / 2;
        var vertices = new List<SceneVertex>();
        foreach (var raw in polygons)
        {
            // To model space: centred, y up (which reverses the winding), then counter-clockwise seen from the front (+z).
            var poly = raw.Select(p => new Vector2(p.X - center.X, center.Y - p.Y) * scale).ToList();
            if (SignedArea(poly) < 0) poly.Reverse();
            float zf = depth / 2, zb = -depth / 2;
            foreach (var (a, b, c) in EarClip(poly))
            {
                vertices.AddRange([new(new(poly[a], zf), Vector3.UnitZ), new(new(poly[b], zf), Vector3.UnitZ), new(new(poly[c], zf), Vector3.UnitZ)]);
                vertices.AddRange([new(new(poly[a], zb), -Vector3.UnitZ), new(new(poly[c], zb), -Vector3.UnitZ), new(new(poly[b], zb), -Vector3.UnitZ)]);
            }
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count], e = q - p;
                var n = Vector3.Normalize(new(e.Y, -e.X, 0));   // outward for a counter-clockwise outline
                SceneVertex V(Vector2 v, float z) => new(new(v, z), n);
                vertices.AddRange([V(p, zf), V(p, zb), V(q, zb), V(p, zf), V(q, zb), V(q, zf)]);
            }
        }
        return new(name, [.. vertices]);
    }

    /// <summary>Drops repeated points (and a closing point equal to the first), which would give zero-area ears and walls.</summary>
    private static List<Vector2> Clean(List<Vector2> points)
    {
        var result = new List<Vector2>();
        foreach (var p in points) if (result.Count == 0 || Vector2.DistanceSquared(result[^1], p) > 1e-6f) result.Add(p);
        while (result.Count > 1 && Vector2.DistanceSquared(result[0], result[^1]) <= 1e-6f) result.RemoveAt(result.Count - 1);
        return result;
    }

    internal static float SignedArea(IReadOnlyList<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; } return a / 2; }

    /// <summary>Ear clipping of a simple counter-clockwise polygon: O(n²), plenty for outlines of a few hundred points.</summary>
    internal static List<(int, int, int)> EarClip(IReadOnlyList<Vector2> p)
    {
        var idx = Enumerable.Range(0, p.Count).ToList(); var tris = new List<(int, int, int)>();
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        int guard = 0;
        while (idx.Count > 3 && guard++ < p.Count * p.Count)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                if (Cross(p[a], p[b], p[c]) <= 1e-9f) continue;   // reflex or flat: not an ear
                bool inside = false;
                foreach (int j in idx)
                {
                    if (j == a || j == b || j == c) continue;
                    if (Cross(p[a], p[b], p[j]) >= 0 && Cross(p[b], p[c], p[j]) >= 0 && Cross(p[c], p[a], p[j]) >= 0) { inside = true; break; }
                }
                if (inside) continue;
                tris.Add((a, b, c)); idx.RemoveAt(i); clipped = true; break;
            }
            if (!clipped)
            {
                // Only collinear remains (or rounding left no clean ear): drop the flattest vertex and go on rather than lose the shape.
                int flat = Enumerable.Range(0, idx.Count).MinBy(i => Math.Abs(Cross(p[idx[(i + idx.Count - 1) % idx.Count]], p[idx[i]], p[idx[(i + 1) % idx.Count]])));
                idx.RemoveAt(flat);
            }
        }
        if (idx.Count == 3) tris.Add((idx[0], idx[1], idx[2]));
        return tris;
    }

    /// <summary>A Wavefront .obj: 'v' positions and 'f' faces (fans for polygons; v, v/vt, v//vn and v/vt/vn forms; negative indices count
    /// back). Normals are recomputed flat per face so a model without them still lights, and the model is centred and scaled like the logo.</summary>
    public static SceneModel FromObj(string text, string name)
    {
        var positions = new List<Vector3>(); var vertices = new List<SceneVertex>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (parts[0] == "v" && parts.Length >= 4) positions.Add(new(F(parts[1]), F(parts[2]), F(parts[3])));
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                var face = parts.Skip(1).Select(v => Index(v, positions.Count)).ToList();
                for (int i = 1; i + 1 < face.Count; i++)
                {
                    Vector3 a = positions[face[0]], b = positions[face[i]], c = positions[face[i + 1]];
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.LengthSquared() < 1e-20f) continue;
                    n = Vector3.Normalize(n);
                    vertices.AddRange([new(a, n), new(b, n), new(c, n)]);
                }
            }
        }
        if (vertices.Count == 0) throw new InvalidDataException("the file has no faces");
        Vector3 min = vertices.Aggregate(new Vector3(float.MaxValue), (m, v) => Vector3.Min(m, v.Position)), max = vertices.Aggregate(new Vector3(float.MinValue), (m, v) => Vector3.Max(m, v.Position));
        float scale = Width / Math.Max(1e-6f, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z))); var center = (min + max) / 2;
        return new(name, [.. vertices.Select(v => v with { Position = (v.Position - center) * scale })]);

        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static int Index(string token, int count)
        {
            int i = int.Parse(token.Split('/')[0], CultureInfo.InvariantCulture);
            int at = i > 0 ? i - 1 : count + i;
            return at >= 0 && at < count ? at : throw new InvalidDataException($"face refers to vertex {i}, which does not exist");
        }
    }
}

/// <summary>SVG path data (M, L, H, V, C, Z and their relative forms, with implicit repeats) as a flat outline; curves become 12 segments.</summary>
internal static class SvgPath
{
    public static List<Vector2> Flatten(string d)
    {
        var points = new List<Vector2>(); var tokens = Tokenize(d); int t = 0; char cmd = 'M'; Vector2 cur = default, start = default;
        float Num() => float.Parse(tokens[t++], CultureInfo.InvariantCulture);
        bool HasNumber() => t < tokens.Count && !char.IsLetter(tokens[t][0]);
        while (t < tokens.Count)
        {
            if (char.IsLetter(tokens[t][0])) cmd = tokens[t++][0];
            bool rel = char.IsLower(cmd);
            Vector2 Pt() { var p = new Vector2(Num(), Num()); return rel ? cur + p : p; }
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M': cur = start = Pt(); points.Add(cur); cmd = rel ? 'l' : 'L'; break;   // further pairs after M are lines
                case 'L': cur = Pt(); points.Add(cur); break;
                case 'H': cur = new(rel ? cur.X + Num() : Num(), cur.Y); points.Add(cur); break;
                case 'V': cur = new(cur.X, rel ? cur.Y + Num() : Num()); points.Add(cur); break;
                case 'C':
                    var p0 = cur; var c1 = Pt(); var c2 = Pt(); var p3 = Pt();
                    for (int i = 1; i <= 12; i++) { float s = i / 12f, u = 1 - s; points.Add(u * u * u * p0 + 3 * u * u * s * c1 + 3 * u * s * s * c2 + s * s * s * p3); }
                    cur = p3; break;
                case 'Z': cur = start; if (HasNumber()) throw new FormatException("numbers after Z"); break;
                default: throw new FormatException($"unsupported path command '{cmd}'");
            }
        }
        return points;
    }

    private static List<string> Tokenize(string d)
    {
        var tokens = new List<string>(); int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c) && c is not ('e' or 'E')) { tokens.Add(c.ToString()); i++; continue; }
            int s = i++;
            while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.' || ((d[i] == '-' || d[i] == '+') && (d[i - 1] == 'e' || d[i - 1] == 'E')) || d[i] is 'e' or 'E')) i++;
            tokens.Add(d[s..i]);
        }
        return tokens;
    }
}
