# Builds src/Mazesta.Core/Health/Checkup/cpu-specs.json from the figures read off the makers' pages (intel-raw.json, amd-raw.json; see README.md).
# Every figure is copied as the page gives it; a figure the page does not give stays out. Nothing is estimated or filled in.
param([string]$Read = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot; $out = Join-Path $here '..\..\src\Mazesta.Core\Health\Checkup\cpu-specs.json'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Mhz([string]$s) { if ($s -match '([\d.]+)\s*GHz') { [int][math]::Round([double]::Parse($Matches[1], $inv) * 1000) } elseif ($s -match '([\d.]+)\s*MHz') { [int][double]::Parse($Matches[1], $inv) } else { $null } }
function Ghz([string]$s) { if ($s -match '([\d.]+)') { [int][math]::Round([double]::Parse($Matches[1], $inv) * 1000) } else { $null } }
function Num([string]$s) { if ($s -match '([\d.]+)') { [int][math]::Round([double]::Parse($Matches[1], $inv)) } else { $null } }
function First($row, [string[]]$keys) { foreach ($k in $keys) { $v = $row.$k; if ($v) { return $v } }; $null }

$cpus = [Collections.Generic.List[object]]::new(); $seen = @{}
function Add($entry) { if ($entry.m -and -not $seen.ContainsKey($entry.m)) { $seen[$entry.m] = $true; $cpus.Add($entry) } }

foreach ($r in (Get-Content (Join-Path $here 'intel-raw.json') -Raw | ConvertFrom-Json)) {
    $key = $null
    if ($r.n -match '^Ultra\s+(X?[3579])\s+Processor\s+(\d{3}[A-Z]{0,2})$') { $key = "ULTRA $($Matches[1]) $($Matches[2])" }
    elseif ($r.num -match '^(i[3579])-(\d{3,5}[A-Z]{0,3})$') { $key = "$($Matches[1])-$($Matches[2])" }
    if (-not $key) { continue }
    Add ([ordered]@{
        m = $key.ToUpperInvariant(); v = 'intel'; seg = $r.seg; c = Num $r.c; t = Num $r.t
        base = Mhz (First $r 'pbase', 'base'); boost = Mhz (First $r 'pturbo', 't2', 'max')
        pbp = Num (First $r 'pbp', 'tdp'); mtp = Num $r.mtp; tj = Num $r.tj; src = 'https://www.intel.com/content/www/us/en/products/sku/' + $r.url })
}
foreach ($r in (Get-Content (Join-Path $here 'amd-raw.json') -Raw | ConvertFrom-Json)) {
    if ($r.name -notmatch 'Ryzen\s+([3579])\s+(PRO\s+)?(\d{4}[A-Z0-9]{0,4})\b') { continue }
    Add ([ordered]@{
        m = "RYZEN $($Matches[1]) $(if ($Matches[2]) { 'PRO ' })$($Matches[3])".ToUpperInvariant(); v = 'amd'; seg = $r.segment; c = Num $r.cores; t = Num $r.threads
        base = Ghz $r.base; boost = Ghz $r.boost; pbp = Num $r.tdp; mtp = $null; tj = Num $r.tj; src = $r.url })
}
$json = [ordered]@{ read = $Read; cpus = $cpus } | ConvertTo-Json -Depth 4 -Compress
# One processor per line, so a change to the table reads as a change to the models it touched.
$json = $json -replace '\},\{"m"', "},`n{`"m`"" -replace '"cpus":\[\{', "`"cpus`":[`n{" -replace '\}\]\}$', "}`n]}"
[IO.File]::WriteAllText([IO.Path]::GetFullPath($out), $json + "`n", [Text.UTF8Encoding]::new($false))
"$($cpus.Count) processors -> $out"
