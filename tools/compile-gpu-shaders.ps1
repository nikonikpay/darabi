# Compiles the raw D3D12 shaders to signed DXIL (.cso) with dxc: the Windows SDK's, or else Microsoft's own DXC NuGet package
# (Microsoft.Direct3D.DXC, fetched once into obj\dxc), so a machine without the SDK can compile them too.
# The .cso files are committed and embedded by Mazesta.Diagnostics.Gpu, so building the app does not need the SDK;
# run this only after changing a .hlsl file.
$ErrorActionPreference = 'Stop'
$dxc = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\dxc.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $dxc) {
    $version = '1.9.2607.13'
    $cache = Join-Path $PSScriptRoot "..\obj\dxc\$version"
    if (-not (Test-Path "$cache\build\native\bin\x64\dxc.exe")) {
        New-Item -ItemType Directory -Force $cache | Out-Null
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.direct3d.dxc/$version/microsoft.direct3d.dxc.$version.nupkg" -OutFile "$cache\dxc.zip"
        Expand-Archive "$cache\dxc.zip" $cache -Force
    }
    $dxc = Get-Item "$cache\build\native\bin\x64\dxc.exe"
    if ((Get-AuthenticodeSignature $dxc.FullName).Status -ne 'Valid') { throw "$($dxc.FullName) is not validly signed." }
}
$dir = Join-Path $PSScriptRoot '..\src\Mazesta.Diagnostics.Gpu\Shaders'
$jobs = @(
    @('Raster.hlsl', 'SceneVS', 'vs_6_0', 'RasterSceneVS.cso'),
    @('Raster.hlsl', 'ScenePS', 'ps_6_0', 'RasterScenePS.cso'),
    @('Raster.hlsl', 'FillVS', 'vs_6_0', 'RasterFillVS.cso'),
    @('Raster.hlsl', 'FillPS', 'ps_6_0', 'RasterFillPS.cso'),
    @('RayQuery.hlsl', 'Main', 'cs_6_5', 'RayQuery.cso'),
    @('GardenRaster.hlsl', 'MainVS', 'vs_6_0', 'GardenMainVS.cso'),
    @('GardenRaster.hlsl', 'ShadowVS', 'vs_6_0', 'GardenShadowVS.cso'),
    @('GardenRaster.hlsl', 'ShadowPS', 'ps_6_0', 'GardenShadowPS.cso'),
    @('GardenRaster.hlsl', 'TintPS', 'ps_6_0', 'GardenTintPS.cso'),
    @('GardenRaster.hlsl', 'OpaquePS', 'ps_6_0', 'GardenOpaquePS.cso'),
    @('GardenRaster.hlsl', 'CutoutPS', 'ps_6_0', 'GardenCutoutPS.cso'),
    @('GardenRaster.hlsl', 'TransparentPS', 'ps_6_0', 'GardenTransparentPS.cso'),
    @('GardenRaster.hlsl', 'SkyVS', 'vs_6_0', 'GardenSkyVS.cso'),
    @('GardenRaster.hlsl', 'SkyPS', 'ps_6_0', 'GardenSkyPS.cso'),
    @('GardenRaster.hlsl', 'AoPS', 'ps_6_0', 'GardenAoPS.cso'),
    @('GardenRaster.hlsl', 'GlowFirstPS', 'ps_6_0', 'GardenGlowFirstPS.cso'),
    @('GardenRaster.hlsl', 'GlowDownPS', 'ps_6_0', 'GardenGlowDownPS.cso'),
    @('GardenRaster.hlsl', 'GlowUpPS', 'ps_6_0', 'GardenGlowUpPS.cso'),
    @('GardenRaster.hlsl', 'LensPS', 'ps_6_0', 'GardenLensPS.cso'),
    @('GardenRaster.hlsl', 'RoundDownPS', 'ps_6_0', 'GardenRoundDownPS.cso'),
    @('SceneOverlay.hlsl', 'OverlayVS', 'vs_6_0', 'SceneOverlayVS.cso'),
    @('SceneOverlay.hlsl', 'OverlayPS', 'ps_6_0', 'SceneOverlayPS.cso'),
    @('GardenRay.hlsl', 'Main', 'cs_6_5', 'GardenRay.cso'),
    @('GardenRay.hlsl', 'Gather', 'cs_6_5', 'GardenGather.cso'),
    @('GardenRay.hlsl', 'Denoise', 'cs_6_5', 'GardenDenoise.cso'),
    @('GardenRay.hlsl', 'Finish', 'cs_6_5', 'GardenFinish.cso'),
    @('GardenRay.hlsl', 'Bake', 'cs_6_5', 'GardenBake.cso')
)
foreach ($j in $jobs) {
    & $dxc.FullName -nologo -O3 -Qstrip_debug -Qstrip_reflect -E $j[1] -T $j[2] -Fo (Join-Path $dir $j[3]) (Join-Path $dir $j[0])
    if ($LASTEXITCODE -ne 0) { throw "dxc failed on $($j[0]) $($j[1])" }
}
Write-Host "Compiled $($jobs.Count) shaders with $($dxc.FullName)"
