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
    @('SceneRaster.hlsl', 'SceneVS', 'vs_6_0', 'SceneRasterVS.cso'),
    @('SceneRaster.hlsl', 'ScenePS', 'ps_6_0', 'SceneRasterPS.cso'),
    @('SceneRaster.hlsl', 'BackgroundVS', 'vs_6_0', 'SceneBackgroundVS.cso'),
    @('SceneRaster.hlsl', 'BackgroundPS', 'ps_6_0', 'SceneBackgroundPS.cso'),
    @('SceneRaster.hlsl', 'FurVS', 'vs_6_0', 'SceneFurVS.cso'),
    @('SceneRaster.hlsl', 'FurPS', 'ps_6_0', 'SceneFurPS.cso'),
    @('SceneRaster.hlsl', 'ParticleVS', 'vs_6_0', 'SceneParticleVS.cso'),
    @('SceneRaster.hlsl', 'ParticlePS', 'ps_6_0', 'SceneParticlePS.cso'),
    @('SceneOverlay.hlsl', 'OverlayVS', 'vs_6_0', 'SceneOverlayVS.cso'),
    @('SceneOverlay.hlsl', 'OverlayPS', 'ps_6_0', 'SceneOverlayPS.cso'),
    @('SceneRay.hlsl', 'Main', 'cs_6_5', 'SceneRay.cso')
)
foreach ($j in $jobs) {
    & $dxc.FullName -nologo -O3 -Qstrip_debug -Qstrip_reflect -E $j[1] -T $j[2] -Fo (Join-Path $dir $j[3]) (Join-Path $dir $j[0])
    if ($LASTEXITCODE -ne 0) { throw "dxc failed on $($j[0]) $($j[1])" }
}
Write-Host "Compiled $($jobs.Count) shaders with $($dxc.FullName)"
