param([string]$Output = '.\artifacts\go-service', [switch]$EmbeddedXray=$true, [switch]$EmbeddedSingBox=$true, [switch]$EmbeddedTun=$true, [switch]$EmbeddedAWG=$true)
$ErrorActionPreference = 'Stop'
if ($EmbeddedTun -and (!$EmbeddedXray -or !$EmbeddedSingBox)) { throw 'Embedded TUN requires both embedded engines.' }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$target = if ([IO.Path]::IsPathRooted($Output)) { [IO.Path]::GetFullPath($Output) } else { [IO.Path]::GetFullPath((Join-Path $project $Output)) }
if (!$target.StartsWith($project + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Go output must be inside this project.' }
$go = & (Join-Path $PSScriptRoot 'Setup-Go.ps1')
$tools = [IO.Path]::GetFullPath((Join-Path $project '..\tools'))
$env:GOCACHE = Join-Path $tools 'go-cache'
$env:GOMODCACHE = Join-Path $tools 'go-mod'
$env:GOTOOLCHAIN = 'local'
$env:CGO_ENABLED = '0'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Push-Location (Join-Path $project 'GoCore')
try {
    $tags = @(); if ($EmbeddedXray) { $tags += 'embeddedxray' }; if ($EmbeddedSingBox) { $tags += 'embeddedsingbox'; $tags += 'with_quic' }
    if ($EmbeddedTun) { $tags += 'embeddedtun'; $tags += 'with_gvisor' }
    if ($EmbeddedAWG) { $tags += 'embeddedawg' }
    $buildTags = if ($tags.Count) { @('-tags',($tags -join ',')) } else { @() }
    & $go build -mod=readonly -trimpath @buildTags -ldflags '-s -w' -o (Join-Path $target 'OneTunnel.Service.exe') ./cmd/onetunnel-service
    if ($LASTEXITCODE -ne 0) { throw 'Go service build failed.' }
    & $go version -m (Join-Path $target 'OneTunnel.Service.exe') | Set-Content (Join-Path $target 'go-build-info.txt') -Encoding UTF8
    $scope = 'external-engines'
    if ($EmbeddedXray -or $EmbeddedSingBox) { $scope = 'proxy-only-experimental' }
    if ($EmbeddedTun) { $scope = 'full-device-experimental-unverified' }
    if ($EmbeddedAWG) { $scope = 'native-and-proxy-partial' }
    if ($EmbeddedAWG -and $EmbeddedXray -and $EmbeddedSingBox -and $EmbeddedTun) { $scope = 'embedded-native-and-proxy' }
    @{Runtime='go';Version='0.3.0';ApiVersion=1;AdapterRequired=$false;EmbeddedXray=[bool]$EmbeddedXray;EmbeddedSingBox=[bool]$EmbeddedSingBox;EmbeddedTun=[bool]$EmbeddedTun;EmbeddedAWG=[bool]$EmbeddedAWG;EmbeddedScope=$scope;EmbeddedInternalNtp=[bool]$EmbeddedSingBox} | ConvertTo-Json | Set-Content (Join-Path $target 'go-runtime.json') -Encoding UTF8
    if ($EmbeddedXray) {
        Copy-Item (Join-Path $env:GOMODCACHE 'github.com\xtls\xray-core@v1.260327.0\LICENSE') (Join-Path $target 'LICENSE.embedded-xray.txt') -Force
    }
    if ($EmbeddedSingBox) {
        Copy-Item (Join-Path $env:GOMODCACHE 'github.com\sagernet\sing-box@v1.14.3\LICENSE') (Join-Path $target 'LICENSE.embedded-sing-box.txt') -Force
    }
    if ($EmbeddedTun -or $EmbeddedAWG) {
        Copy-Item (Join-Path $project 'third-party\notices\LICENSE.wintun.txt') (Join-Path $target 'LICENSE.embedded-wintun.txt') -Force
    }
    if ($EmbeddedXray -or $EmbeddedSingBox -or $EmbeddedAWG) {
        $modules = @(& $go list -mod=readonly @buildTags -deps -f '{{with .Module}}{{.Path}}|{{.Version}}|{{.Dir}}{{end}}' ./cmd/onetunnel-service | Where-Object { $_ -match '\|' } | Sort-Object -Unique)
        if ($LASTEXITCODE -ne 0) { throw 'Dependency notice enumeration failed.' }
        $notices = @(); $noticeRoot = Join-Path $target 'third-party'
        foreach ($entry in $modules) {
            $parts = $entry -split '\|',3
            if ($parts[0] -eq 'onetunnel/core') { continue }
            $moduleFolder = ($parts[0] + '@' + $parts[1]) -replace '[^A-Za-z0-9._@-]','_'
            $moduleTarget = Join-Path $noticeRoot $moduleFolder
            New-Item -ItemType Directory -Force -Path $moduleTarget | Out-Null
            $licenses = @(Get-ChildItem -LiteralPath $parts[2] -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|COPYRIGHT)(\..*)?$' })
            foreach ($license in $licenses) { Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $moduleTarget $license.Name) -Force }
            $notices += @{Module=$parts[0];Version=$parts[1];Directory=('third-party/'+$moduleFolder);Notices=@($licenses.Name)}
        }
        $notices | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $target 'embedded-dependencies.json') -Encoding UTF8
    }
    Copy-Item (Join-Path (Split-Path (Split-Path $go)) 'LICENSE') (Join-Path $target 'LICENSE.go.txt') -Force
    Copy-Item (Join-Path $env:GOMODCACHE 'github.com\!microsoft\go-winio@v0.6.2\LICENSE') (Join-Path $target 'LICENSE.go-winio.txt') -Force
    Copy-Item (Join-Path $env:GOMODCACHE 'golang.org\x\sys@v0.49.0\LICENSE') (Join-Path $target 'LICENSE.x-sys.txt') -Force
    Copy-Item (Join-Path $env:GOMODCACHE 'golang.org\x\net@v0.61.0\LICENSE') (Join-Path $target 'LICENSE.x-net.txt') -Force
    Copy-Item (Join-Path $env:GOMODCACHE 'golang.org\x\text@v0.43.0\LICENSE') (Join-Path $target 'LICENSE.x-text.txt') -Force
} finally { Pop-Location }
