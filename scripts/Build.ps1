param([string]$Name = 'OneTunnel-win-x64')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $project
try {
    if ($Name -notmatch '^OneTunnel[-a-zA-Z0-9.]+$') { throw 'Invalid package name' }
    if (!(Test-Path -LiteralPath '.\assets\emoji\1f1f7-1f1fa.png')) { throw 'Run scripts\Setup-Emoji.ps1 before packaging.' }
    $output = Join-Path '.\dist' $Name
    dotnet publish .\OneTunnel.csproj -c Release -r win-x64 --self-contained true -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    & .\scripts\Build-GoCore.ps1 -Output (Join-Path $output 'service')
    New-Item -ItemType Directory -Path (Join-Path $output 'scripts') -Force | Out-Null
    Copy-Item .\scripts\Install-Service.ps1 -Destination (Join-Path $output 'scripts')
    Copy-Item .\README.md,.\THIRD-PARTY.md -Destination $output
    Copy-Item -LiteralPath .\docs -Destination $output -Recurse -Force
    Compress-Archive -Path $output -DestinationPath "$output.zip" -Force
    Get-FileHash "$output.zip" -Algorithm SHA256 | Format-List
} finally { Pop-Location }
