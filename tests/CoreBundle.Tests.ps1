param([string]$Validator = (Join-Path $PSScriptRoot '..\scripts\Validate-CoreBundle.ps1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('onetunnel-bundle-tests-' + [guid]::NewGuid().ToString('N'))
$revision = '1111111111111111111111111111111111111111'
$required = @('OneTunnel.Profiles.exe','service/OneTunnel.Service.exe','service/libcronet.dll',
    'service/go-runtime.json','service/go-build-info.txt','service/embedded-dependencies.json',
    'service/LICENSE.go.txt','service/LICENSE.cronet.txt','service/LICENSE.embedded-wintun.txt',
    'service/LICENSE.embedded-xray.txt','service/LICENSE.embedded-sing-box.txt','LICENSE.uniseg.txt')

function Save-Manifest($bundle, $manifest) {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Encoding UTF8
}
function Update-Manifest($bundle) {
    $manifest = Get-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Raw | ConvertFrom-Json
    $manifest.Files = @(Get-ChildItem -LiteralPath $bundle -Recurse -File | Where-Object Name -ne 'core-bundle.json' | ForEach-Object {
        @{Path=$_.FullName.Substring($bundle.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    Save-Manifest $bundle $manifest
}
function New-Fixture([string]$name) {
    $bundle = Join-Path $testRoot $name
    New-Item -ItemType Directory -Path (Join-Path $bundle 'service') -Force | Out-Null
    foreach ($relative in $required) {
        $path = Join-Path $bundle $relative
        if ($relative -match '\.(exe|dll)$') {
            # These bytes have just enough PE metadata to inspect; they are never executed.
            $bytes = New-Object byte[] 128
            $bytes[0]=0x4d; $bytes[1]=0x5a; $bytes[0x3c]=64
            $bytes[64]=0x50; $bytes[65]=0x45; $bytes[68]=0x64; $bytes[69]=0x86
            [IO.File]::WriteAllBytes($path, $bytes)
        } else { 'synthetic notice or metadata' | Set-Content -LiteralPath $path -Encoding UTF8 }
    }
    @{Runtime='go';Version='0.5.0';ApiVersion=1;SourceRevision=$revision;Dirty=$false;OS='windows';Architecture='amd64';
        AdapterRequired=$false;EmbeddedScope='embedded-native-and-proxy';EmbeddedAWG=$true;EmbeddedXray=$true;
        EmbeddedSingBox=$true;EmbeddedTun=$true;EmbeddedNaive=$true} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'service/go-runtime.json') -Encoding UTF8
    Save-Manifest $bundle @{SchemaVersion=1;Version='0.5.0';ApiVersion=1;OS='windows';Architecture='amd64';SourceRevision=$revision;Dirty=$false;Files=@()}
    Update-Manifest $bundle
    return $bundle
}
$script:passed = 0
$script:skipped = 0
$junctions = @()
function Must-Reject([string]$name, [scriptblock]$operation, [string]$expectedMessage = '') {
    $rejected = $false
    try { & $operation | Out-Null } catch {
        if ($expectedMessage -and $_.Exception.Message -notlike "*$expectedMessage*") {
            throw "Unexpected rejection for ${name}: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (!$rejected) { throw "Validator accepted invalid bundle: $name" }
    $script:passed++

}
try {
    $valid = New-Fixture 'valid'
    $identity = & $Validator -Bundle $valid -ExpectedRevision $revision
    if ($identity.SourceRevision -cne $revision) { throw 'Valid bundle identity was lost.' }
    $script:passed++
    $reference = Join-Path $testRoot 'core-reference.json'
    $pin = @{SchemaVersion=1;Repository='OneTunnel-Core';SourceRevision=$revision;Version='0.5.0';ApiVersion=1;OS='windows';Architecture='amd64'}
    $pin | ConvertTo-Json | Set-Content -LiteralPath $reference -Encoding UTF8
    & $Validator -Bundle $valid -Reference $reference -Quiet
    $script:passed++
    foreach ($property in @('SourceRevision','Version','ApiVersion','OS','Architecture','Repository','SchemaVersion')) {
        $changed = $pin.Clone()
        switch ($property) {
            'SourceRevision' { $changed[$property] = '2' * 40 }
            'Version' { $changed[$property] = '0.5.1' }
            'ApiVersion' { $changed[$property] = 2 }
            'OS' { $changed[$property] = 'linux' }
            'Architecture' { $changed[$property] = 'arm64' }
            'Repository' { $changed[$property] = 'Unexpected-Core' }
            'SchemaVersion' { $changed[$property] = '1' }
        }
        $changed | ConvertTo-Json | Set-Content -LiteralPath $reference -Encoding UTF8
        Must-Reject "mismatched pinned $property" { & $Validator -Bundle $valid -Reference $reference }
    }
    $pin | ConvertTo-Json | Set-Content -LiteralPath $reference -Encoding UTF8
    $dirty = New-Fixture 'dirty-pinned'
    $manifest = Get-Content -LiteralPath (Join-Path $dirty 'core-bundle.json') -Raw | ConvertFrom-Json
    $manifest.Dirty = $true
    Save-Manifest $dirty $manifest
    $runtime = Get-Content -LiteralPath (Join-Path $dirty 'service/go-runtime.json') -Raw | ConvertFrom-Json
    $runtime.Dirty = $true
    $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dirty 'service/go-runtime.json') -Encoding UTF8
    Update-Manifest $dirty
    & $Validator -Bundle $dirty -Quiet
    Must-Reject 'dirty pinned bundle' { & $Validator -Bundle $dirty -Reference $reference } 'pinned clean source revision'
    Must-Reject 'missing pinned reference' { & $Validator -Bundle $valid -Reference (Join-Path $testRoot 'missing-reference.json') }
    Must-Reject 'missing bundle' { & $Validator -Bundle (Join-Path $testRoot 'missing') }
    Must-Reject 'wrong selected revision' { & $Validator -Bundle $valid -ExpectedRevision ('2' * 40) }
    Must-Reject 'wrong API' { & $Validator -Bundle $valid -ExpectedApiVersion 2 }
    Must-Reject 'unsupported architecture' { & $Validator -Bundle $valid -ExpectedArchitecture arm64 }
    foreach ($badPath in @('../escape.exe','/absolute.exe','C:/escape.exe','service\escape.exe','service/../escape.exe','service//escape.exe','service/name.','service/name ','service/file:stream',
        'service/CON','service/nul.txt','service/PRN','service/AUX.dat','service/COM1.exe','service/LPT9.txt',
        'service/a<b','service/a>b','service/a"b','service/a|b','service/a?b','service/a*b','service/a[b]')) {
        $bundle = New-Fixture ('path-' + $script:passed)
        $manifest = Get-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Raw | ConvertFrom-Json
        $manifest.Files[0].Path = $badPath
        Save-Manifest $bundle $manifest
        Must-Reject "unsafe path $badPath" { & $Validator -Bundle $bundle } 'Invalid or duplicate core bundle file entry.'
    }
    foreach ($property in @('SchemaVersion','ApiVersion','Version','SourceRevision','Dirty')) {
        $bundle = New-Fixture ('manifest-type-' + $property)
        $manifest = Get-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Raw | ConvertFrom-Json
        if ($property -eq 'Dirty') { $manifest.$property = 'false' }
        elseif ($property -eq 'Version' -or $property -eq 'SourceRevision') { $manifest.$property = 1 }
        else { $manifest.$property = '1' }
        Save-Manifest $bundle $manifest
        Must-Reject "manifest property type $property" { & $Validator -Bundle $bundle } 'Incompatible core bundle identity'
    }
    foreach ($property in @('Path','Sha256')) {
        $bundle = New-Fixture ('file-type-' + $property)
        $manifest = Get-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Raw | ConvertFrom-Json
        $manifest.Files[0].$property = 1
        Save-Manifest $bundle $manifest
        Must-Reject "file property type $property" { & $Validator -Bundle $bundle } 'Invalid or duplicate core bundle file entry.'
    }
    foreach ($flag in @('AdapterRequired','EmbeddedAWG','EmbeddedXray','EmbeddedSingBox','EmbeddedTun','EmbeddedNaive','Dirty')) {
        $bundle = New-Fixture ('runtime-type-' + $flag)
        $runtime = Get-Content -LiteralPath (Join-Path $bundle 'service/go-runtime.json') -Raw | ConvertFrom-Json
        $runtime.$flag = 'true'
        $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'service/go-runtime.json') -Encoding UTF8
        Update-Manifest $bundle
        Must-Reject "runtime flag type $flag" { & $Validator -Bundle $bundle } 'invalid flag type'
    }
    $bundle = New-Fixture 'duplicate'
    $manifest = Get-Content -LiteralPath (Join-Path $bundle 'core-bundle.json') -Raw | ConvertFrom-Json
    $manifest.Files += $manifest.Files[0]
    Save-Manifest $bundle $manifest
    Must-Reject 'duplicate entry' { & $Validator -Bundle $bundle }
    $bundle = New-Fixture 'corrupt'
    'changed' | Add-Content -LiteralPath (Join-Path $bundle 'OneTunnel.Profiles.exe')
    Must-Reject 'corrupt checksum' { & $Validator -Bundle $bundle }
    $bundle = New-Fixture 'extra'
    'private data must not pass packaging' | Set-Content -LiteralPath (Join-Path $bundle 'unexpected.txt')
    Must-Reject 'undeclared file' { & $Validator -Bundle $bundle }
    $bundle = New-Fixture 'missing-required'
    Remove-Item -LiteralPath (Join-Path $bundle 'service/libcronet.dll')
    Update-Manifest $bundle
    Must-Reject 'required native dependency absent' { & $Validator -Bundle $bundle }
    $bundle = New-Fixture 'runtime'
    $runtime = Get-Content -LiteralPath (Join-Path $bundle 'service/go-runtime.json') -Raw | ConvertFrom-Json
    $runtime.EmbeddedNaive = $false
    $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'service/go-runtime.json') -Encoding UTF8
    Update-Manifest $bundle
    Must-Reject 'silently reduced engine set' { & $Validator -Bundle $bundle }
    $bundle = New-Fixture 'wrong-machine'
    $bytes = [IO.File]::ReadAllBytes((Join-Path $bundle 'service/libcronet.dll'))
    $bytes[68]=0x64; $bytes[69]=0xaa
    [IO.File]::WriteAllBytes((Join-Path $bundle 'service/libcronet.dll'), $bytes)
    Update-Manifest $bundle
    Must-Reject 'ARM64 DLL in amd64 bundle' { & $Validator -Bundle $bundle }
    $descendantBundle = New-Fixture 'junction-descendant'
    foreach ($link in @((Join-Path $testRoot 'junction-root'), (Join-Path $descendantBundle 'junction-child'))) {
        $created = $false
        try {
            New-Item -ItemType Junction -Path $link -Target $valid -ErrorAction Stop | Out-Null
            $created = $true
            $junctions += $link
        } catch {
            $script:skipped++
            Write-Output "SKIP: junction creation unavailable: $($_.Exception.Message)"
        }
        if ($created) {
            $candidate = if ($link -eq (Join-Path $testRoot 'junction-root')) { $link } else { $descendantBundle }
            Must-Reject 'junction traversal' { & $Validator -Bundle $candidate } 'reparse point'
        }
    }
    Write-Output "PASS: $script:passed synthetic core bundle validation checks; skipped=$script:skipped; no binaries executed."
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^onetunnel-bundle-tests-[0-9a-f]{32}$') { throw 'Unsafe test cleanup path.' }
    foreach ($link in $junctions) {
        $linkPath = [IO.Path]::GetFullPath($link)
        if (!$linkPath.StartsWith($resolved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe junction cleanup path.' }
        if (!((Get-Item -LiteralPath $linkPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected junction during cleanup.' }
        # Non-recursive deletion removes the junction itself, never its target.
        [IO.Directory]::Delete($linkPath)
    }
    if ($junctions.Count -and !(Test-Path -LiteralPath (Join-Path $valid 'core-bundle.json'))) { throw 'Junction cleanup changed its target.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
