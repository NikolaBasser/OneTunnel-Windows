$ErrorActionPreference = 'Stop'
$version = 'go1.27.3'
$tools = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\tools'))
$target = Join-Path $tools $version
$executable = Join-Path $target 'go\bin\go.exe'
if (Test-Path -LiteralPath $executable) { Write-Output $executable; return }
$releases = Invoke-RestMethod 'https://go.dev/dl/?mode=json&include=all'
$file = $releases | Where-Object version -eq $version | Select-Object -ExpandProperty files | Where-Object { $_.os -eq 'windows' -and $_.arch -eq 'amd64' -and $_.kind -eq 'archive' } | Select-Object -First 1
if (!$file -or $file.filename -notmatch '^go[0-9.]+\.windows-amd64\.zip$') { throw 'Pinned official Go release not found.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
$archive = Join-Path $target $file.filename
Invoke-WebRequest ('https://go.dev/dl/' + $file.filename) -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Go archive checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
@{version=$version;url=('https://go.dev/dl/'+$file.filename);sha256=$file.sha256} | ConvertTo-Json | Set-Content (Join-Path $target 'provenance.json') -Encoding UTF8
Write-Output $executable
