param([Parameter(Mandatory=$true)][string]$OwnerSid)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (!([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator token required.' }
$owner = New-Object Security.Principal.SecurityIdentifier($OwnerSid)
if ($owner.Value -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { throw 'A local interactive user SID is required.' }
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\service'))
if (!(Test-Path -LiteralPath (Join-Path $source 'OneTunnel.Service.exe'))) { throw 'Use the complete OneTunnel package.' }
if (!(Test-Path -LiteralPath (Join-Path $source 'go-runtime.json'))) { throw 'Use the complete native Go core package.' }
$destination = Join-Path $env:ProgramFiles 'OneTunnel\Core'
$runtime = Join-Path $env:ProgramData ('OneTunnel\Service\' + $owner.Value)
# Updating a live or unclean connection would replace its trusted engines.
if ((Test-Path -LiteralPath (Join-Path $runtime 'session.json')) -or (Test-Path -LiteralPath (Join-Path $runtime 'kill-switch.json'))) { throw 'Disconnect or recover the current service connection before updating.' }
if (Test-Path -LiteralPath (Join-Path $destination 'service-owner.json')) {
    $previous = Get-Content -LiteralPath (Join-Path $destination 'service-owner.json') -Raw | ConvertFrom-Json
    if ($previous.OwnerSid -ne $owner.Value) { throw 'The installed service belongs to another Windows user.' }
}
foreach ($base in @((Join-Path $env:ProgramFiles 'OneTunnel'), $destination, (Join-Path $env:ProgramData 'OneTunnel'), (Join-Path $env:ProgramData 'OneTunnel\Service'), $runtime)) {
    if ((Test-Path -LiteralPath $base) -and ((Get-Item -LiteralPath $base -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse directories are not allowed.' }
    New-Item -ItemType Directory -Path $base -Force | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
    }
    if ($base.StartsWith($env:ProgramFiles,[StringComparison]::OrdinalIgnoreCase)) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($owner,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
    }
    Set-Acl -LiteralPath $base -AclObject $acl
}
if (Get-ChildItem -LiteralPath $destination -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse files are not allowed in the service installation.' }
if (Get-ChildItem -LiteralPath $runtime -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse files are not allowed in service runtime.' }
# A pre-created runtime must not retain explicit user-write ACLs below its root.
foreach ($item in @(Get-ChildItem -LiteralPath $runtime -Recurse -Force)) {
    $itemAcl = Get-Acl -LiteralPath $item.FullName
    $itemAcl.SetAccessRuleProtection($false,$false)
    foreach ($rule in @($itemAcl.Access | Where-Object { !$_.IsInherited })) { $itemAcl.RemoveAccessRuleSpecific($rule) }
    Set-Acl -LiteralPath $item.FullName -AclObject $itemAcl
}
$service = Get-Service -Name OneTunnelCore -ErrorAction SilentlyContinue
if ($service) { Stop-Service -Name OneTunnelCore; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(120)) }
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
# The native core has no managed service runtime. Remove only obsolete files
# directly in this verified service directory; engine DLLs are in engines/.
foreach ($obsolete in @(Get-ChildItem -LiteralPath $destination -File | Where-Object {
    $_.Extension -in @('.dll','.pdb') -or $_.Name -like 'OneTunnel.Adapter*' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json'
})) {
    if ([IO.Path]::GetDirectoryName($obsolete.FullName) -ne [IO.Path]::GetFullPath($destination)) { throw 'Unexpected cleanup target.' }
    Remove-Item -LiteralPath $obsolete.FullName -Force
}
@{OwnerSid=$owner.Value} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'service-owner.json') -Encoding UTF8
# Retire only the two fixed engine files after the idle-state check and stop.
$metadata=Get-Content -LiteralPath (Join-Path $source 'go-runtime.json') -Raw | ConvertFrom-Json
$retired=@('wireguard.exe','wg.exe')
if($metadata.EmbeddedAWG -and $metadata.EmbeddedXray -and $metadata.EmbeddedSingBox -and $metadata.EmbeddedTun){$retired+=@('amneziawg.exe','awg.exe','xray.exe','sing-box.exe','wintun.dll')}
foreach ($oldEngine in $retired) {
    $oldPath = Join-Path $destination ('engines\' + $oldEngine)
    if (Test-Path -LiteralPath $oldPath) { Remove-Item -LiteralPath $oldPath -Force }
}
$binary = '"' + (Join-Path $destination 'OneTunnel.Service.exe') + '"'
if (!$service) { & sc.exe create OneTunnelCore binPath= $binary start= auto obj= LocalSystem DisplayName= 'OneTunnel Core' | Out-Null }
else { & sc.exe config OneTunnelCore binPath= $binary start= auto obj= LocalSystem | Out-Null }
if ($LASTEXITCODE -ne 0) { throw 'Service registration failed.' }
& sc.exe description OneTunnelCore 'Manages OneTunnel connections, DNS, routes and persistent traffic protection.' | Out-Null
& sc.exe sdset OneTunnelCore ('D:P(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCRPLO;;;' + $owner.Value + ')') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Service access policy failed.' }
# No automatic reconnect or automatic removal of protection after a crash.
Start-Service -Name OneTunnelCore
(Get-Service OneTunnelCore).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
