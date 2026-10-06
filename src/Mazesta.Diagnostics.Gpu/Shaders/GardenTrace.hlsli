// Rays through the garden (DXR 1.1 inline RayQuery), for whoever sends them: the Direct3D test with ray tracing switched on
// (GardenRaster.hlsl with RT defined: its shadows and its mirror images) and the light volume's baker (GardenBake.hlsl).
// Leaves are cut out of their cards during traversal. The includer declares the scene - Scene (the acceleration structure
// GardenAccel.cs builds), Instances, Materials, Meshes, Subs, Vertices, Indices, Textures - and names its sampler TraceSampler.

static const uint MaskVisible = 1, MaskShadow = 2, MaskTint = 4;   // what a ray meets; what blocks light; the stained panes, which colour it

// T, B: the directions the texture's u and v run in over the triangle; Lod: the mip level at which a texel is the size of a pixel there
struct Hit { float3 P; float3 N; float2 Uv; uint Material; float3 T; float3 B; float Lod; uint Flags; };

float Rand(inout uint seed) { seed = Hash(seed); return (seed >> 8) * (1.0 / 16777216.0); }
float3 InBall(inout uint seed)
{
    float z = Rand(seed) * 2 - 1, a = Rand(seed) * 2 * Pi, r = sqrt(1 - z * z);
    return float3(r * cos(a), r * sin(a), z) * pow(Rand(seed), 1.0 / 3);
}

uint3 Triangle(SubInfo s, uint prim) { return Indices.Load3((s.IndexStart + prim * 3) * 4); }

float2 UvAt(uint instance, uint geometry, uint prim, float2 bary)
{
    MeshInfo m = Meshes[Instances[instance].Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    uint3 t = Triangle(s, prim);
    float2 a = DecodeUv(Vertices.Load((m.BaseVertex + t.x) * 16 + 12)), b = DecodeUv(Vertices.Load((m.BaseVertex + t.y) * 16 + 12)), c = DecodeUv(Vertices.Load((m.BaseVertex + t.z) * 16 + 12));
    Material mat = Materials[s.Material];
    return (a + (b - a) * bary.x + (c - a) * bary.y) * (mat.Pattern.x > 0 ? mat.Pattern.xy : 1);
}

// A leaf card is a hit only where its texture is opaque.
bool AlphaPass(uint instance, uint geometry, uint prim, float2 bary)
{
    MeshInfo m = Meshes[Instances[instance].Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    Material mat = Materials[s.Material];
    if (mat.Texture < 0) return true;
    return Textures.SampleLevel(TraceSampler, float3(UvAt(instance, geometry, prim, bary), mat.Texture), 1).a >= 0.5;
}

Hit Fetch(uint instance, uint geometry, uint prim, float2 bary, float3 p, float dist)
{
    Instance inst = Instances[instance]; MeshInfo m = Meshes[inst.Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    uint3 t = Triangle(s, prim);
    uint3 o = (m.BaseVertex + t) * 16;
    float3 na = DecodeNormal(Vertices.Load(o.x + 8)), nb = DecodeNormal(Vertices.Load(o.y + 8)), nc = DecodeNormal(Vertices.Load(o.z + 8));
    float2 ua = DecodeUv(Vertices.Load(o.x + 12)), ub = DecodeUv(Vertices.Load(o.y + 12)), uc = DecodeUv(Vertices.Load(o.z + 12));
    Hit h; h.P = p; h.Material = s.Material; h.Flags = inst.Flags;
    h.N = normalize(mul(Rotation(inst), na + (nb - na) * bary.x + (nc - na) * bary.y));
    if (inst.Flags & FLogo) h.N = LogoTurn(h.N);
    h.Uv = ua + (ub - ua) * bary.x + (uc - ua) * bary.y;
    float3 pa = DecodePosition(Vertices.Load2(o.x), 0, m.Extent), e1 = mul(Rotation(inst), DecodePosition(Vertices.Load2(o.y), 0, m.Extent) - pa), e2 = mul(Rotation(inst), DecodePosition(Vertices.Load2(o.z), 0, m.Extent) - pa);
    float2 d1 = ub - ua, d2 = uc - ua; float det = d1.x * d2.y - d2.x * d1.y, area = length(cross(e1, e2));
    h.T = 0; h.B = 0; h.Lod = 0;
    uint w, hh, layers, levels; Textures.GetDimensions(0, w, hh, layers, levels);
    Material mat = Materials[s.Material]; bool world; float2 uv = TexCoords(mat, h.Uv, p, h.N, world);
    if (world)   // laid by world position: u and v run along the world's own axes on this face
    {
        float3 a = abs(h.N);
        h.T = a.y > 0.6 || a.x <= 0.6 ? float3(1, 0, 0) : float3(0, 0, 1); h.B = a.y > 0.6 ? float3(0, 0, 1) : float3(0, 1, 0);
        h.Lod = clamp(log2(max(mat.Pattern.z * w * dist * 2 * TanHalfFovY / Height, 1e-6)) - 0.5, 0, levels - 1);
    }
    else if (abs(det) > 1e-12 && area > 1e-12)
    {
        h.T = (e1 * d2.y - e2 * d1.y) / det; h.B = (e2 * d1.x - e1 * d2.x) / det;
        if (inst.Flags & FLogo) { h.T = LogoTurn(h.T); h.B = LogoTurn(h.B); }
        float repeats = mat.Kind <= KCutout && mat.Pattern.x > 0 ? max(mat.Pattern.x, mat.Pattern.y) : 1;
        // texels a metre (the triangle's share of the texture over its size) times the metres a pixel spans at this distance
        h.Lod = clamp(log2(max(sqrt(abs(det) / area) * repeats * w * dist * 2 * TanHalfFovY / Height, 1e-6)) - 0.5, 0, levels - 1);
    }
    h.Uv = uv;
    return h;
}

bool Trace(float3 origin, float3 dir, float tmax, uint mask, out Hit hit)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, mask, ray);
    while (q.Proceed())
        if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && AlphaPass(q.CandidateInstanceID(), q.CandidateGeometryIndex(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    hit = (Hit)0;
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return false;
    hit = Fetch(q.CommittedInstanceID(), q.CommittedGeometryIndex(), q.CommittedPrimitiveIndex(), q.CommittedTriangleBarycentrics(), origin + dir * q.CommittedRayT(), q.CommittedRayT());
    return true;
}

bool Occluded(float3 origin, float3 dir, float tmax)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, MaskShadow, ray);
    while (q.Proceed())
        if (AlphaPass(q.CandidateInstanceID(), q.CandidateGeometryIndex(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    return q.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
}

Surface SurfaceAt(Hit h, float3 v)
{
    Material m = Materials[h.Material];
    float4 texel = m.Texture >= 0 ? Textures.SampleLevel(TraceSampler, float3(h.Uv, m.Texture), h.Lod) : 1;
    float3 n = h.N; if (dot(n, v) < 0) n = -n;
    Surface s = MaterialSurface(m, h.P, n, texel);
    if (m.NormalTexture >= 0) Relief(s, h.T, h.B, Textures.SampleLevel(TraceSampler, float3(h.Uv, m.NormalTexture), h.Lod));
    if (h.Flags & FLogo) s.Emission += s.Albedo * Logo.w;   // the logo is a lit sign at night
    return s;
}

// What colour light keeps on its way along a ray: that of the first stained pane it passes (white when it meets none).
float3 Through(float3 origin, float3 dir, float tmax)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, MaskTint, ray); q.Proceed();
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return 1;
    MeshInfo m = Meshes[Instances[q.CommittedInstanceID()].Mesh];
    Material mat = Materials[Subs[m.FirstSubmesh + q.CommittedGeometryIndex()].Material];
    return mat.LightTint > 0 ? StainTint(mat) : 1;
}
