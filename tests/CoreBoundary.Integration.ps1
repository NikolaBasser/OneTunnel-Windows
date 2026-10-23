param(
    [Parameter(Mandatory=$true)][string]$CoreBundlePath,
    [string]$Project = ''
)
$ErrorActionPreference = 'Stop'
if (!$Project) { $Project = Join-Path $PSScriptRoot '..\OneTunnel.csproj' }
$projectPath = [IO.Path]::GetFullPath($Project)
$projectRoot = Split-Path -Parent $projectPath
$bundle = [IO.Path]::GetFullPath($CoreBundlePath)
$manifest = & (Join-Path $projectRoot 'scripts\Validate-CoreBundle.ps1') -Bundle $bundle
$output = Join-Path $projectRoot ('artifacts\core-copy-check-' + [guid]::NewGuid().ToString('N'))
function Build-Consumer {
    & dotnet build $projectPath --no-restore -c Release -r win-x64 -o $output "-p:CoreBundlePath=$bundle" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows core consumer build failed.' }
}
Build-Consumer
foreach ($relative in @('OneTunnel.Profiles.exe','service/OneTunnel.Service.exe','service/libcronet.dll')) {
    $path = Join-Path $output $relative
    [IO.File]::WriteAllText($path, 'Synthetic stale output; never execute.')
    [IO.File]::SetLastWriteTimeUtc($path, [DateTime]::UtcNow.AddDays(1))
}
Build-Consumer
foreach ($file in $manifest.Files) {
    $path = Join-Path $output $file.Path
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.Sha256) {
        throw "Windows output does not contain the selected core file: $($file.Path)"
    }
}
Write-Output 'PASS: Windows consumer replaces newer stale binaries and preserves every selected bundle checksum.'
