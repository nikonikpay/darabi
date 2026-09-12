param([string]$Exe = "artifacts/Mazesta-Test/MazestaTest.exe", [int]$SettleSeconds = 300, [int]$SampleSeconds = 30)
$p = Start-Process -FilePath $Exe -PassThru -Verb RunAs
Start-Sleep -Seconds $SettleSeconds
$p.Refresh(); $cpu0 = $p.TotalProcessorTime; $t0 = Get-Date
Start-Sleep -Seconds $SampleSeconds
$p.Refresh(); $cpu1 = $p.TotalProcessorTime; $t1 = Get-Date
$cpuPct = ($cpu1 - $cpu0).TotalSeconds / ($t1 - $t0).TotalSeconds / [Environment]::ProcessorCount * 100
[pscustomobject]@{ WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); IdleCpuPercent = [math]::Round($cpuPct, 2); Threads = $p.Threads.Count } | Format-List
Stop-Process $p
