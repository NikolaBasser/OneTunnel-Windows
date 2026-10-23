param(
    [Parameter(Mandatory=$true)][string]$Bundle,
    [int]$ExpectedApiVersion = 1,
    [string]$ExpectedOS = 'windows',
    [string]$ExpectedArchitecture = 'amd64',
    [string]$ExpectedRevision = '',
    [string]$Reference = '',
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($Bundle)
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Core bundle directory is missing.' }
$ancestor = Get-Item -LiteralPath $root -Force
while ($null -ne $ancestor) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Core bundle paths must not traverse reparse points.' }
    $ancestor = $ancestor.Parent
}
$manifestPath = Join-Path $root 'core-bundle.json'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Core bundle manifest is missing.' }
$items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Core bundle contains a reparse point.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (($manifest.SchemaVersion -isnot [int] -and $manifest.SchemaVersion -isnot [long]) -or
    ($manifest.ApiVersion -isnot [int] -and $manifest.ApiVersion -isnot [long]) -or
    $manifest.Version -isnot [string] -or $manifest.SourceRevision -isnot [string] -or
    $manifest.SchemaVersion -ne 1 -or $manifest.ApiVersion -ne $ExpectedApiVersion -or
    $manifest.OS -cne $ExpectedOS -or $manifest.Architecture -cne $ExpectedArchitecture -or
    $manifest.Version -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.SourceRevision -notmatch '^[0-9a-f]{40}$' -or $manifest.Dirty -isnot [bool]) {
    throw 'Incompatible core bundle identity, API, or target.'
}
if ($ExpectedOS -cne 'windows' -or $ExpectedArchitecture -cne 'amd64') { throw 'Only Windows amd64 core bundles are currently supported.' }
if ($ExpectedRevision -and $manifest.SourceRevision -cne $ExpectedRevision) { throw 'Core source revision does not match the selected revision.' }
if ($Reference) {
    $pin = Get-Content -LiteralPath $Reference -Raw | ConvertFrom-Json
    if (($pin.SchemaVersion -isnot [int] -and $pin.SchemaVersion -isnot [long]) -or $pin.SchemaVersion -ne 1 -or
        $pin.Repository -cne 'OneTunnel-Core' -or $pin.SourceRevision -isnot [string] -or
        $pin.SourceRevision -cnotmatch '^[0-9a-f]{40}$' -or $pin.Version -isnot [string] -or
        $pin.Version -notmatch '^\d+\.\d+\.\d+$' -or
        ($pin.ApiVersion -isnot [int] -and $pin.ApiVersion -isnot [long]) -or
        $pin.ApiVersion -ne $ExpectedApiVersion -or $pin.OS -cne $ExpectedOS -or
        $pin.Architecture -cne $ExpectedArchitecture) { throw 'Invalid pinned core reference.' }
    if ($manifest.SourceRevision -cne $pin.SourceRevision -or $manifest.Version -cne $pin.Version -or
        $manifest.Dirty) { throw 'Core bundle must match the pinned clean source revision and version.' }
}
$declared = @{}
foreach ($entry in @($manifest.Files)) {
    $relative = [string]$entry.Path
    if ($entry.Path -isnot [string] -or $entry.Sha256 -isnot [string] -or
        !$relative -or $relative -cne $relative.Trim() -or $relative -match '[\\:]|(^|/)\.{1,2}(/|$)|(^/)|[\x00-\x1f]' -or
        $relative -match '[<>"|?*\[\]]' -or $relative -match '(^|/)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.[^/]*)?(/|$)' -or
        $relative -match '(^|/)[^/]*[. ](/|$)' -or $relative -match '//|/$' -or
        $relative -ieq 'core-bundle.json' -or $declared.ContainsKey($relative) -or
        [string]$entry.Sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid or duplicate core bundle file entry.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (!$path.StartsWith($root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Core bundle file is missing or outside its root.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry.Sha256) { throw "Core bundle checksum mismatch: $relative" }
    $declared[$relative] = $true
}
$actual = @($items | Where-Object { !$_.PSIsContainer })
if ($actual.Count -ne $declared.Count + 1) { throw 'Core bundle contains undeclared files.' }
foreach ($file in $actual) {
    $relative = $file.FullName.Substring($root.TrimEnd('\','/').Length + 1).Replace('\','/')
    if ($relative -ine 'core-bundle.json' -and !$declared.ContainsKey($relative)) { throw 'Core bundle contains an undeclared file.' }
}
$required = @('OneTunnel.Profiles.exe','service/OneTunnel.Service.exe','service/libcronet.dll',
    'service/go-runtime.json','service/go-build-info.txt','service/embedded-dependencies.json',
    'service/LICENSE.go.txt','service/LICENSE.cronet.txt','service/LICENSE.embedded-wintun.txt',
    'service/LICENSE.embedded-xray.txt','service/LICENSE.embedded-sing-box.txt','LICENSE.uniseg.txt')
foreach ($relative in $required) { if (!$declared.ContainsKey($relative)) { throw "Required core bundle file is missing: $relative" } }
$runtime = Get-Content -LiteralPath (Join-Path $root 'service/go-runtime.json') -Raw | ConvertFrom-Json
foreach ($flag in @('AdapterRequired','EmbeddedAWG','EmbeddedXray','EmbeddedSingBox','EmbeddedTun','EmbeddedNaive','Dirty')) {
    if ($runtime.$flag -isnot [bool]) { throw 'Core runtime metadata contains an invalid flag type.' }
}
if ($runtime.Runtime -cne 'go' -or $runtime.Version -cne $manifest.Version -or $runtime.ApiVersion -ne $manifest.ApiVersion -or
    $runtime.SourceRevision -cne $manifest.SourceRevision -or $runtime.Dirty -ne $manifest.Dirty -or
    $runtime.OS -cne $manifest.OS -or $runtime.Architecture -cne $manifest.Architecture -or
    $runtime.AdapterRequired -ne $false -or $runtime.EmbeddedScope -cne 'embedded-native-and-proxy' -or
    $runtime.EmbeddedAWG -ne $true -or $runtime.EmbeddedXray -ne $true -or $runtime.EmbeddedSingBox -ne $true -or
    $runtime.EmbeddedTun -ne $true -or $runtime.EmbeddedNaive -ne $true) { throw 'Core runtime metadata does not match the complete bundle.' }
foreach ($relative in @('OneTunnel.Profiles.exe','service/OneTunnel.Service.exe','service/libcronet.dll')) {
    $stream = [IO.File]::OpenRead((Join-Path $root $relative))
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Core binary has no DOS header.' }
        $stream.Position = 0x3c
        $offset = $reader.ReadUInt32()
        if ($offset -gt $stream.Length - 6) { throw 'Core binary has an invalid PE header.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Core binary is not Windows amd64.' }
    } finally { $reader.Dispose() }
}
if (!$Quiet) { Write-Output $manifest }
