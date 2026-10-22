param([Parameter(Mandatory=$true)][string]$Directory,[int]$WorkerId,[int]$Seconds=100)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Directory)
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$root.StartsWith((Join-Path $workspace 'artifacts')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture directory must be inside project artifacts'}
for($part=Get-Item -LiteralPath $root;$part -and $part.FullName.Length -ge $workspace.Length;$part=$part.Parent){if($part.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse fixture path forbidden'}}
$worker=Get-Process -Id $WorkerId -ErrorAction Stop
if([IO.Path]::GetFullPath($worker.Path) -ne (Join-Path $root 'worker.test.exe')){throw 'Unexpected fixture process'}
$birth=$worker.StartTime.ToUniversalTime().Ticks
[IO.File]::WriteAllText((Join-Path $root 'guard-ready'),[string]$WorkerId)
$deadline=[DateTime]::UtcNow.AddSeconds($Seconds)
while([DateTime]::UtcNow -lt $deadline){
 if(Test-Path -LiteralPath (Join-Path $root 'guard-complete')){exit 0}
 if(Test-Path -LiteralPath (Join-Path $root 'crash-ready')){break}
 $current=Get-Process -Id $WorkerId -ErrorAction SilentlyContinue
 if(!$current -or $current.StartTime.ToUniversalTime().Ticks -ne $birth){break}
 Start-Sleep -Milliseconds 200
}
$current=Get-Process -Id $WorkerId -ErrorAction SilentlyContinue
if($current -and $current.StartTime.ToUniversalTime().Ticks -eq $birth){Stop-Process -Id $WorkerId -Force}
if(Test-Path -LiteralPath (Join-Path $root 'crash-ready')){
 $env:ONETUNNEL_LIVE_CLIENT='crashed'
 & (Join-Path $root 'recovery.test.exe') '-test.run=^TestLiveIndependentClient$' '-test.timeout=20s' *> (Join-Path $root 'crash-client.log')
}
$env:ONETUNNEL_LIVE_RECOVER='1'
for($attempt=0;$attempt -lt 3;$attempt++){
 & (Join-Path $root 'recovery.test.exe') '-test.run=^TestLiveIndependentRecovery$' '-test.timeout=25s' *> (Join-Path $root 'guard-recovery.log')
 if($LASTEXITCODE -eq 0){
  try {if((Invoke-WebRequest -Uri https://www.gstatic.com/generate_204 -TimeoutSec 8).StatusCode -eq 204){[IO.File]::WriteAllText((Join-Path $root 'guard-recovered'),'network restored');exit 0}}catch{}
 }
}
[IO.File]::WriteAllText((Join-Path $root 'guard-failed'),'recovery needs attention')
exit 1
