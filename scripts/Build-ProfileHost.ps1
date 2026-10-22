$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$go=& (Join-Path $PSScriptRoot 'Setup-Go.ps1')
$env:GOCACHE=Join-Path $project '..\tools\go-cache'
$env:GOMODCACHE=Join-Path $project '..\tools\go-mod'
$env:GOTOOLCHAIN='local'
$env:CGO_ENABLED='0'
Push-Location (Join-Path $project 'GoCore')
try{
    $output=Join-Path $project 'artifacts\profile-host\OneTunnel.Profiles.exe'
    New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
    & $go build -mod=readonly -trimpath -ldflags '-s -w' -o $output ./cmd/onetunnel-profiles
    if($LASTEXITCODE -ne 0){throw 'Go profile host build failed'}
    $unicodeModule=& $go list -m -mod=readonly -f '{{.Dir}}' github.com/rivo/uniseg
    if($LASTEXITCODE -ne 0 -or !$unicodeModule){throw 'Unicode dependency directory unavailable'}
    Copy-Item -LiteralPath (Join-Path $unicodeModule 'LICENSE.txt') -Destination (Join-Path $project 'assets\LICENSE.uniseg.txt') -Force
}finally{Pop-Location}
