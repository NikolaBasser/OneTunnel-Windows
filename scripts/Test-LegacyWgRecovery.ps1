$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$process=Start-Process -FilePath (Join-Path $project 'artifacts\platform-tests-0.10.1.exe') -ArgumentList @('-test.run','^TestLegacyWGServiceRemovalWithoutEngine$','-test.v') -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $project 'artifacts\legacy-wg-recovery-check.log') -RedirectStandardError (Join-Path $project 'artifacts\legacy-wg-recovery-error.log')
@{Success=($process.ExitCode -eq 0)} | ConvertTo-Json | Set-Content (Join-Path $project 'artifacts\legacy-wg-recovery-check.json')
exit $process.ExitCode
