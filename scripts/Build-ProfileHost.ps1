$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$go=& (Join-Path $PSScriptRoot 'Setup-Go.ps1')
$env:GOCACHE=Join-Path $project '..\tools\go-cache'
$env:GOMODCACHE=Join-Path $project '..\tools\go-mod'
$env:GOTOOLCHAIN='local'
$env:CGO_ENABLED='0'
Push-Location (Join-Path $project 'GoCore')
try{
    & $go build -mod=readonly -trimpath -ldflags '-s -w' -o (Join-Path $project 'OneTunnel.Profiles.exe') ./cmd/onetunnel-profiles
    if($LASTEXITCODE -ne 0){throw 'Go profile host build failed'}
    Copy-Item -LiteralPath (Join-Path $env:GOMODCACHE 'github.com\rivo\uniseg@v0.4.7\LICENSE.txt') -Destination (Join-Path $project 'assets\LICENSE.uniseg.txt') -Force
}finally{Pop-Location}
