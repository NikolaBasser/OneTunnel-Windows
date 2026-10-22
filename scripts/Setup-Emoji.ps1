$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$commit = 'b6b55fef1e8636b540a6d016a4729ca8cdf2e60b'
$archiveHash = '9adcbb1eb790b1c91285ea2c218fd62a6e9e041efff448c59827e3397037e0dd'
$cache = Join-Path $project 'assets\downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$zip = Join-Path $cache 'twemoji-17.0.3.zip'
if (!(Test-Path -LiteralPath $zip)) { Invoke-WebRequest -TimeoutSec 120 -Headers @{'User-Agent'='OneTunnel-Prototype'} -Uri "https://api.github.com/repos/jdecked/twemoji/zipball/$commit" -OutFile $zip }
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveHash) { throw 'Twemoji archive checksum mismatch.' }
Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $cache 'twemoji') -Force
$root = (Get-ChildItem (Join-Path $cache 'twemoji') -Directory | Select-Object -First 1).FullName
$destination = Join-Path $project 'assets\emoji'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $root 'assets\72x72\*.png') $destination
Copy-Item (Join-Path $root 'LICENSE-GRAPHICS') (Join-Path $project 'assets\LICENSE.twemoji.txt')
@{version='17.0.3';commit=$commit;source="https://github.com/jdecked/twemoji/tree/$commit";archive_sha256=$archiveHash} | ConvertTo-Json | Set-Content (Join-Path $project 'assets\emoji-provenance.json') -Encoding utf8
