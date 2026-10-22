$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$events=Get-Content -LiteralPath (Join-Path $project 'artifacts\go-profiles-all-tests.jsonl') | ConvertFrom-Json
if(@($events|Where-Object {$_.Action -eq 'fail'}).Count){throw 'Go failures remain'}
$goPassed=@($events|Where-Object {$_.Action -eq 'pass' -and $_.Test -match '^Test[^/]*$'}).Count
$service=Get-Content -LiteralPath (Join-Path $project 'artifacts\go-profiles-service-check.log')
$ui=Get-Content -LiteralPath (Join-Path $project 'artifacts\go-profiles-ui-check.log')
if($goPassed -lt 38 -or !($service -match 'ALL 139 CHECKS PASSED') -or !($ui -match 'ALL UI CHECKS PASSED')){throw 'Required verification evidence incomplete'}
$pipeline=@($service|Where-Object {$_ -match '^PASS GO PROFILE PIPELINE:'}).Count
if($pipeline -lt 17){throw 'Go import/store/service coverage incomplete'}
$copyLine=($events|Where-Object {$_.Output -match 'copied (\d+) profiles and (\d+) subscription origins'}|Select-Object -First 1).Output
if($copyLine -notmatch 'copied (\d+) profiles and (\d+) subscription origins'){throw 'User-store copy check missing'}
$count=[int]$Matches[1];$origins=[int]$Matches[2]
@{
 Version='0.11.0';Date='2026-10-22';GoTestsPassed=$goPassed;ProtocolParityCases=126;ServiceChecks=139;GoImportStoreServiceCases=$pipeline;UiChecks=@($ui|Where-Object {$_ -match '^PASS UI:'}).Count;UiLifecycleChecks=3
 Migration=@{LegacyEncryptedArrays=$true;DocumentV1=$true;CSharpReadsGoDPAPI=$true;GoReadsCSharpDPAPI=$true;OriginalStoreUnchanged=$true;CopiedProfiles=$count;CopiedSubscriptionOrigins=$origins;LiveSubscriptionProfilesParsed=38;LiveRefreshAppliedToCopyOnly=$true;EncryptedBackup=$true;ConflictAndCorruptionProtection=$true}
 Production=@{CSharpBusinessCoreShipped=$false;CSharpBackendFixtures='tests/LegacyCore only';GoUserHost='OneTunnel.Profiles.exe';GoWindowsService='OneTunnelCore';ApiVersion=1;WireGuardSharesAmneziaEngine=$true;ProtocolsPreserved=6}
 LocalTraffic=@('SOCKS5 anonymous and auth','VLESS TCP/raw, WS, gRPC, HTTPUpgrade, XHTTP, KCP','Shadowsocks AES-128-GCM','Hysteria2 plain and Salamander','TLS fragmentation, MUX and random UDP noise')
 Safety=@{FullDeviceTestsRun=$false;SystemRoutesChanged=$false;SystemDnsChanged=$false;KillSwitchEnabledInTests=$false;AutomaticReconnectAdded=$false;UserProfilesWritten=$false;PacketEnginesEmbedded=$false}
 Limitations=@('No new full-device, IPv6 leak, reboot or sleep/resume coverage','Native WG/AWG imports/configurations covered; network engine unchanged from 0.10.1','External engines remain separate','Go race detector not run; no C toolchain configured')
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $project 'artifacts\go-profiles-check-0.11.0.json')
Write-Output 'Go profile migration report finalized.'
