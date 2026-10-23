param([string]$Name = 'OneTunnel-win-x64', [Parameter(Mandatory=$true)][string]$CoreBundlePath)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $project
try {
    if ($Name -notmatch '^OneTunnel[-a-zA-Z0-9.]+$') { throw 'Invalid package name' }
    $bundle = [IO.Path]::GetFullPath($CoreBundlePath)
    $null = & (Join-Path $PSScriptRoot 'Validate-CoreBundle.ps1') -Bundle $bundle
    if (!(Test-Path -LiteralPath '.\assets\emoji\1f1f7-1f1fa.png')) { throw 'Run scripts\Setup-Emoji.ps1 before packaging.' }
    $output = Join-Path '.\dist' $Name
    if (Test-Path -LiteralPath $output) { throw 'Package output already exists. Choose a new package name to prevent stale files.' }
    dotnet publish .\OneTunnel.csproj -c Release -r win-x64 --self-contained true -o $output --nologo "-p:CoreBundlePath=$bundle"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    New-Item -ItemType Directory -Path (Join-Path $output 'scripts') -Force | Out-Null
    Copy-Item .\scripts\Install-Service.ps1 -Destination (Join-Path $output 'scripts')
    Copy-Item .\BUILD.txt,.\THIRD-PARTY-NOTICES.txt -Destination $output
    Compress-Archive -Path $output -DestinationPath "$output.zip" -Force
    Get-FileHash "$output.zip" -Algorithm SHA256 | Format-List
} finally { Pop-Location }
