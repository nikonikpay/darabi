# Compiles the raw D3D12 benchmark shaders to signed DXIL (.cso) with the Windows SDK's dxc.
# The .cso files are committed and embedded by Mazesta.Diagnostics.Gpu, so building the app does not need the SDK;
# run this only after changing a .hlsl file.
$ErrorActionPreference = 'Stop'
$dxc = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\dxc.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $dxc) { throw 'dxc.exe not found: install the Windows 10/11 SDK.' }
$dir = Join-Path $PSScriptRoot '..\src\Mazesta.Diagnostics.Gpu\Shaders'
$jobs = @(
    @('Raster.hlsl', 'SceneVS', 'vs_6_0', 'RasterSceneVS.cso'),
    @('Raster.hlsl', 'ScenePS', 'ps_6_0', 'RasterScenePS.cso'),
    @('Raster.hlsl', 'FillVS', 'vs_6_0', 'RasterFillVS.cso'),
    @('Raster.hlsl', 'FillPS', 'ps_6_0', 'RasterFillPS.cso'),
    @('RayQuery.hlsl', 'Main', 'cs_6_5', 'RayQuery.cso')
)
foreach ($j in $jobs) {
    & $dxc.FullName -nologo -O3 -Qstrip_debug -Qstrip_reflect -E $j[1] -T $j[2] -Fo (Join-Path $dir $j[3]) (Join-Path $dir $j[0])
    if ($LASTEXITCODE -ne 0) { throw "dxc failed on $($j[0]) $($j[1])" }
}
Write-Host "Compiled $($jobs.Count) shaders with $($dxc.FullName)"
