param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\external-engines'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$cache = Join-Path $Destination 'downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$headers = @{ 'User-Agent' = 'OneTunnel-Prototype' }
$records = @()
function Get-Asset([string]$Repo, [string]$Tag, [string]$Name) {
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
    $asset = $release.assets | Where-Object name -EQ $Name | Select-Object -First 1
    if (!$asset) { throw "Asset not found: $Name" }
    $path = Join-Path $cache $Name
    if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest -Headers $headers -Uri $asset.browser_download_url -OutFile $path }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if (!$asset.digest -or $asset.digest -ne "sha256:$hash") { throw "Missing or mismatched official asset SHA256: $Name" }
    $script:records += [ordered]@{ name=$Name; source=$asset.browser_download_url; sha256=$hash; verified='GitHub release asset digest' }
    return $path
}
function Expand-Msi([string]$Path, [string]$Target) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $p = Start-Process msiexec.exe -ArgumentList @('/a', ('"' + $Path + '"'), '/qn', ('TARGETDIR="' + $Target + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { throw "Administrative MSI extraction failed ($($p.ExitCode)). No client installation was requested." }
}
$sing = Get-Asset 'SagerNet/sing-box' 'v1.14.3' 'sing-box-1.14.3-windows-amd64.zip'
Expand-Archive -LiteralPath $sing -DestinationPath (Join-Path $cache 'sing-box') -Force
Get-ChildItem (Join-Path $cache 'sing-box') -Filter sing-box.exe -Recurse | Select-Object -First 1 | Copy-Item -Destination $Destination
$xray = Get-Asset 'XTLS/Xray-core' 'v26.3.27' 'Xray-windows-64.zip'
Expand-Archive -LiteralPath $xray -DestinationPath (Join-Path $cache 'xray') -Force
Copy-Item -LiteralPath (Join-Path $cache 'xray\xray.exe') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $cache 'xray\LICENSE') -Destination (Join-Path $Destination 'LICENSE.xray.txt')
$awg = Get-Asset 'amnezia-vpn/amneziawg-windows-client' '3.1.0' 'amneziawg-amd64-3.1.0.msi'
Expand-Msi $awg (Join-Path $cache 'awg-extracted')
foreach ($name in @('amneziawg.exe','awg.exe','wintun.dll')) { Get-ChildItem (Join-Path $cache 'awg-extracted') -Filter $name -Recurse | Select-Object -First 1 | Copy-Item -Destination $Destination }
foreach ($file in @('sing-box.exe','xray.exe','amneziawg.exe','awg.exe','wintun.dll')) { if (!(Test-Path -LiteralPath (Join-Path $Destination $file))) { throw "Missing engine file: $file" } }
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'provenance.json') -Encoding utf8
$licenses = @{
    'LICENSE.sing-box.txt'='https://raw.githubusercontent.com/SagerNet/sing-box/v1.14.3/LICENSE'
    'LICENSE.amneziawg.txt'='https://raw.githubusercontent.com/amnezia-vpn/amneziawg-windows-client/3.1.0/COPYING'
    'LICENSE.wireguard.txt'='https://git.zx2c4.com/wireguard-windows/plain/COPYING?h=v1.1.1'
    'LICENSE.wintun.txt'='https://raw.githubusercontent.com/WireGuard/wintun/master/prebuilt-binaries-license.txt'
    'LICENSE.GPL-3.0.txt'='https://raw.githubusercontent.com/spdx/license-list-data/main/text/GPL-3.0-or-later.txt'
}
foreach ($name in $licenses.Keys) { Invoke-WebRequest -TimeoutSec 30 -Uri $licenses[$name] -OutFile (Join-Path $Destination $name) }
Write-Host 'Engines ready. Only MSI administrative extraction was performed; no VPN service was installed.'
