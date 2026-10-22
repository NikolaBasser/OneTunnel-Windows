param([Parameter(Mandatory=$true)][string]$Directory,[int]$Index=0,[switch]$Drill,[switch]$Crash)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Directory)
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$root.StartsWith((Join-Path $workspace 'artifacts')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture directory must be inside project artifacts'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(!([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Elevated test runner required'}
for($part=Get-Item -LiteralPath $root;$part -and $part.FullName.Length -ge $workspace.Length;$part=$part.Parent){if($part.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse fixture path forbidden'}}
foreach($name in @('worker.test.exe','recovery.test.exe','profiles-machine.bin')){if(!(Test-Path -LiteralPath (Join-Path $root $name))){throw 'Complete protected fixture required'}}
foreach($name in @('guard-ready','guard-complete','guard-recovered','guard-failed','crash-ready')){
 $file=Join-Path $root $name
 if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}
}
foreach($mode in @('baseline','connected','broken','restored','crashed')){
 $file=Join-Path $root "client-$mode.json"
 if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}
}
New-Item -ItemType Directory -Path (Join-Path $root 'private-runtime\runtime') -Force | Out-Null
$env:ONETUNNEL_LIVE_DEVICE='1'
$env:ONETUNNEL_LIVE_INDEX=[string]$Index
$seconds=100
if($Drill){$env:ONETUNNEL_LIVE_GUARD_DRILL='1';$seconds=3}else{Remove-Item Env:\ONETUNNEL_LIVE_GUARD_DRILL -ErrorAction SilentlyContinue}
if($Crash){$env:ONETUNNEL_LIVE_CRASH_DRILL='1'}else{Remove-Item Env:\ONETUNNEL_LIVE_CRASH_DRILL -ErrorAction SilentlyContinue}
$capture=$false
if(!$Drill){
 pktmon filter list *> (Join-Path $root "filters-$Index.txt")
 pktmon start --capture --comp nics --pkt-size 128 --file-size 32 --file-name (Join-Path $root "network-$Index.etl") *> (Join-Path $root "capture-start-$Index.txt")
 if($LASTEXITCODE -ne 0){throw 'Independent packet capture could not start; no network test started'}
 $capture=$true
}
try {
$worker=Start-Process -FilePath (Join-Path $root 'worker.test.exe') -ArgumentList @('-test.run=^TestLiveDeviceGuarded$','-test.timeout=95s','-test.v') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root "worker-$Index.log") -RedirectStandardError (Join-Path $root "worker-$Index.err")
$guard=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $PSScriptRoot 'EmbeddedSafetyWatch.ps1')+'"'),'-WorkerId',$worker.Id,'-Directory',('"'+$root+'"'),'-Seconds',$seconds) -WindowStyle Hidden -PassThru
if(!$worker.WaitForExit(125000)){throw 'Guarded worker did not finish'}
if(!$guard.WaitForExit(45000)){throw 'Independent recovery guard did not finish'}
$passed=Select-String -LiteralPath (Join-Path $root "worker-$Index.log") -Pattern '^--- PASS: TestLiveDeviceGuarded' -Quiet
$result=[ordered]@{Index=$Index;WorkerPassed=[bool]$passed;GuardExit=$guard.ExitCode;Restored=(Test-Path -LiteralPath (Join-Path $root 'guard-complete'));EmergencyRecovery=(Test-Path -LiteralPath (Join-Path $root 'guard-recovered'));RecoveryFailed=(Test-Path -LiteralPath (Join-Path $root 'guard-failed'))}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "runner-$Index.json") -Encoding UTF8
foreach($mode in @('baseline','connected','broken','restored','crashed')){if(Test-Path -LiteralPath (Join-Path $root "client-$mode.json")){Copy-Item -LiteralPath (Join-Path $root "client-$mode.json") -Destination (Join-Path $root "client-$Index-$mode.json") -Force}}
Copy-Item -LiteralPath (Join-Path $root 'client-input.json') -Destination (Join-Path $root "client-input-$Index.json") -Force -ErrorAction SilentlyContinue
} finally {
 if($capture){
  pktmon counters *> (Join-Path $root "capture-counters-$Index.txt")
  pktmon stop *> (Join-Path $root "capture-stop-$Index.txt")
  pktmon etl2pcap (Join-Path $root "network-$Index.etl") --out (Join-Path $root "network-$Index.pcapng") *> (Join-Path $root "capture-convert-$Index.txt")
 }
}
