param([Parameter(Mandatory=$true)][string]$OwnerSid)
$ErrorActionPreference='Stop'
$hostExecutable=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\OneTunnel.Profiles.exe'))
if(!(Test-Path -LiteralPath $hostExecutable)){throw 'Complete OneTunnel package required'}
& $hostExecutable --install-service $OwnerSid
if($LASTEXITCODE -ne 0){throw 'Native Go service installation failed; inspect idle/recovery state and UAC rights'}
