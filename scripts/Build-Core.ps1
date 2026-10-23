param([string]$Output = 'artifacts/core-bundle')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$go = & (Join-Path $PSScriptRoot 'Setup-Go.ps1')
$env:GOCACHE = [IO.Path]::GetFullPath((Join-Path $project '..\tools\go-cache'))
$env:GOMODCACHE = [IO.Path]::GetFullPath((Join-Path $project '..\tools\go-mod'))
$target = if ([IO.Path]::IsPathRooted($Output)) { [IO.Path]::GetFullPath($Output) } else { [IO.Path]::GetFullPath((Join-Path $project $Output)) }
& (Join-Path $project 'GoCore\build.ps1') -Output $target -GoExecutable $go
if ($LASTEXITCODE -ne 0) { throw 'Core bundle build failed.' }
