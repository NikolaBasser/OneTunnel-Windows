# OneTunnel for Windows

One Windows interface for VPN and proxy connections, powered by the embedded
[OneTunnel Go core](https://github.com/NikolaBasser/OneTunnel-Core).

## Protocols

AmneziaWG, WireGuard, VLESS, SOCKS5, Hysteria2, Shadowsocks, HTTP CONNECT and
HTTPS proxies, Trojan, TUIC, AnyTLS, and NaiveProxy.

## Features

- Local proxy and whole-device connection modes.
- Whole-device traffic protection with explicit disconnect and recovery controls.
- Profile and subscription import, collapsible configuration groups, and country flags/emoji.
- Saved subscription groups with provider-reported usage/expiry and a manual refresh preview that requires explicit approval before applying changes.
- Optional automatic reconnection and compatible backup servers within a group.
- Group availability checks with cancellation and per-server results.
- Desktop workspace with profile search by name, group or protocol, and a separate event log.
- Notification-area status and quick connection actions, with opt-in status notifications available in the window and tray for the current session.
- An explicit minimize-to-tray button with a temporary Ctrl+Shift+O restore shortcut and window recovery after an Explorer restart.
- Import/update subscriptions through the existing import editor; navigation preserves unfinished edits.
- User profiles encrypted with Windows DPAPI.
- Persistent favorites, a favorites-only filter, and stored/name/protocol view
  sorting powered by Go. Collapsible groups remain contiguous.
- A Groups workspace for renaming and confirmed deletion, plus expand/collapse-all
  controls that also apply to groups hidden by search or favorites.
- Go-generated diagnostics preview with explicit copy or JSON save, excluding
  profile credentials, names, server addresses and subscription URLs.

The interface runs in the user session. A separate authenticated Go service manages
engines and system networking. Closing the window leaves the service connection active.
Ordinary minimization retains the taskbar. The separate minimize-to-tray button
hides the window; restore it from the tray or press Ctrl+Shift+O. The shortcut is
reserved only while hidden. If Windows cannot register it, the window stays visible
and reports the conflict. An Explorer restart restores an explicitly hidden window.
Status notifications are off by default and can be enabled in the window or tray;
the choice resets when the application exits.
The Events page can generate a local diagnostics report. Review it before copying
or saving. Its identity describes the bundled profile host; engine availability
is a package hint. It does not inspect the installed service or test connectivity,
the kill switch or DNS/IPv6 leaks. Reports are not generated or uploaded automatically.
Favorite choices survive application restarts and subscription updates that retain
profile IDs. Filters and sort choices reset per session; the stored server order
used by fallback connections remains unchanged. Name sorting compares lowercase
Unicode values within groups; it does not provide locale-specific collation.
Group renaming keeps profile IDs, favorites and saved subscription details.
Deleting a group removes its profiles and local subscription refresh source; it
does not delete a remote subscription. Rename/delete reject a changed store
snapshot and require a reload before retry. Group changes follow the existing
Go editing policy and remain unavailable while a connection needs protection.
Its recovery entry opens the recovery view without executing recovery automatically.
Persistent traffic protection remains active after a connection failure and during
reconnection. Explicit disconnect/recovery controls release owned protection.

## Feature implementation status

Reviewed on 2026-10-27 against the pinned core and recorded live checks. **Ready** means
the stated scope is implemented and checked; **Partial** includes implemented
features whose current-build system/network validation is incomplete. None of
the original roadmap rows is wholly unimplemented. This source status does not
describe the older 0.1.0 release binary or the installed service.

| Feature | Stage | Implemented scope and remaining validation |
| --- | --- | --- |
| Split routing | Partial, live scope verified | IPv4 shared-TUN domain/CIDR/process direct and reject rules, including initial streams, pass repeated live checks. IPv6 domain/process reject and domain/process/CIDR direct pass five additional controlled runs; earlier default and /128 reject also pass. Direct rules remain intentional exceptions during proxy loss while physical-interface bypass stays blocked. Compound domain-set/process/TCP/port rules select a distinct secondary server; a port mismatch stays on the primary. Provider-specific policy variants and multiple-uplink matrices remain separate. |
| Auto reconnect | Partial | Proxy and shared-TUN outage/recovery pass with fresh statistics sessions. The actual server Ethernet interface was disabled for 45.55 seconds and recovered. Physical sleep/wake and transitions between real Wi-Fi networks remain. |
| Backup servers | Partial, shared TUN verified | Controlled same-group shared-TUN fallback passes outage, promotion and restoration. Earlier IPv4 checks recorded 442 physical TCP probes without bypass. An additional dual-stack failover run records 441 physical IPv4 and 441 physical IPv6 probes, zero bypasses/errors, successful IPv6 HTTPS on the promoted backup and a fresh statistics session. This does not certify every native transport or provider. |
| Group checks | Ready, bounded scope | Cancellable HTTPS group probes and separate WG/AWG imported-DNS latency. All 38 saved proxy checks completed with listener cleanup:7 HTTPS successes and31 negative outcomes; one saved WG and AWG DNS probe passed. Other protocols have synthetic fixture coverage. Saved TUN policies are configuration-validated only; server availability and leak protection are separate. |
| DNS settings | Partial, bounded live checks | DoH, DoT, domain-selected alternate DoH and fresh-name resolution pass. Physical port-53 captures to the configured IPv4/IPv6 resolvers include a positive baseline and zero active protected packets. WG/AWG controlled peers pass configured DNS and public TLS. Arbitrary applications, alternative DNS channels and all provider variants are not certified. |
| LAN access | Partial, primary uplink verified | Main-PC HTTP access to two real private-LAN devices passes AllowLAN; explicit CIDR reject takes priority. Physical external TCP 443/53 remains blocked. Multiple uplinks, more-specific physical routes and every native-provider variant remain outside this proof. |
| Auto connect | Partial | Current-user startup registry enable/state/idempotence/disable and foreign-edit preservation pass three repetitions. Four real Public/Private classification cases pass with modeled controller/session timing. Actual interactive Windows login and complete automatic connection remain. |
| Windows system proxy | Partial, WinINET verified | Real WinINET HTTPS, listener/owner loss, production observer recovery, foreign-edit preservation and exact restoration pass three repetitions. Detached child survival outside the owner job also passes. The acceptance launcher was injected; the complete interactive UI-to-watchdog launch path remains. |
| Statistics and diagnostics | Partial, live runtime verified | Positive shared-TUN counters and fresh reconnect/fallback sessions pass. Controlled encrypted WG/AWG peers pass IPv4/IPv6 public TLS and configured DNS with growing native and TUN counters, outage protection and restoration. The native IPv6 proof uses IPv6 inner payload over loopback IPv4 UDP peers. Remote-provider and native IPv6 endpoint compatibility remain separate. |
| Subscription management | Ready, scoped | Preview/apply/cancel, provider-reported usage/expiry and opt-in scheduled previews while the app is open and idle. No background refresh after app exit or automatic apply. |
| Portable backup | Ready, isolated checks | Password-encrypted export and full-replacement restore with preview; wrong-password, tamper and stale-token tests pass. Merge restore is not implemented. |
| Tray and notifications | Ready, isolated checks | Explicit hide/restore, Go-owned actions, profile selection and opt-in notifications. Selecting a profile does not connect. |
| Kill switch lifecycle | Partial, crash/reboot verified | IPv4/IPv6 outage and real reboot retain protection until independent recovery. An additional IPv6 forced crash confirms both worker and guard process exits, 27 persistent WFP filters, blocked HTTPS/physical TCP and no DNS response, followed by first-attempt restoration. Genuine installed-service SCM Stop and a real OS reboot with the active pinned service pass. Before independent boot recovery, the journal and owned WFP objects are already absent and both address-family baselines work. The remaining transport matrix is separate. |
| Offline emergency recovery | Implemented, x64 live verified | Standalone x64 recovery works after a real failure and at startup, with owner-scoped transactional cleanup. ARM64 builds/checksums pass; actual Windows ARM64 execution remains untested. |

Production-tag Go tests/vet, Windows x64 build, pinned-core integration, 59
synthetic bundle checks and 219 isolated WPF checks passed. Full pinned-core
consumer CI and Linux x64/ARM64 portable-package CI also passed. Windows ARM64
cross-compilation is not device/runtime proof. Live blocking tests used an
authorized dedicated Windows Server 2025 VM, immutable per-run ownership and
independent absolute-deadline/boot recovery. The old failed canary remains
disabled. No new release was published.

The six additional 2026-10-27 acceptance cases used controlled SOCKS gateways
and a fixed public IPv6 HTTPS target, with positive functional baselines before
and after protection. Exact process handles prove the forced exits. Each routing
run checks normal stop and independent deadline recovery; the final server has
no test WFP objects or tasks and working HTTPS. These new cases did not repeat
physical DNS packet capture and do not certify every DNS channel or provider.

Four follow-up VM cases also pass: dual-stack failover, controlled WG IPv6,
controlled AWG 3.1 IPv6, and an OS reboot of the active unmodified installed
service. Native peers forward real public IPv6 TLS through encrypted userspace
tunnels; their outer UDP endpoints are IPv4 loopback, not remote IPv6 providers.
For reboot, a controlled non-forwarding SOCKS fixture establishes the full-TUN
state and 27 owned filters; successful in-tunnel WAN access is not asserted.
Boot snapshots precede independent recovery and show normal shutdown cleanup;
OS boot timestamps and SCM events corroborate the actual reboot. The final
service is stopped with exit code zero, test WFP/tasks are absent, and networking
is restored. New cases do not add physical DNS packet-capture certification.

## Platform and versions

**Windows 10/11 x64** is the supported application target. Native Windows ARM64
packages are not available. Linux, Android, and iOS work belongs to the separate
core/platform projects; this repository contains the Windows application.

The application starts its independent release series at **0.1.0**. The pinned core
uses its own version series; application 0.1.0 currently consumes core 0.5.0 through
[core-reference.json](core-reference.json). A core version and an application version
do not need to match.

## Download and build

Windows packages are available to authorized repository members on the
[Releases page](https://github.com/NikolaBasser/OneTunnel-Windows/releases).
The package includes the .NET runtime, user-mode profile host, and Go service.
Service installation requires administrator permission.

[BUILD.txt](BUILD.txt) contains source build, installation, and launch instructions.
Windows builds consume an explicitly selected clean core bundle whose revision,
version, API, target, and file integrity match the repository reference. They do not
compile or download Go binaries implicitly.

## Verification and notices

CI distinguishes synthetic source checks from full builds against the pinned core.
The manually dispatched integration build defaults to the public
`NikolaBasser/OneTunnel-Core` repository and the job's read-only GitHub token.
`ONETUNNEL_CORE_REPOSITORY` can select a trusted alternative;
`CORE_READ_TOKEN` supplies optional read access to a private source. Both paths
build the exact pinned core revision without persisting checkout credentials.
The pinned-core integration job also runs isolated WPF workspace checks with
synthetic profiles. These checks do not connect a tunnel or modify the installed service.
An availability result is not a blanket guarantee about a remote server, and native
WireGuard/AmneziaWG group checks probe an eligible imported DNS address through the embedded userspace tunnel without changing OS networking; otherwise they validate configuration only.

[Security policy](.github/SECURITY.md) ·
[Third-party notices](THIRD-PARTY-NOTICES.txt) ·
[Original dependency notices](third-party/notices/)

Upstream components retain their respective licenses. No additional project-wide
license grant is declared by this README.

Group check results can be sorted by full check duration and filtered by outcome.
An explicit best-result action selects a successful network check without connecting.
WireGuard/AmneziaWG configuration-only and DNS-only results are excluded from HTTPS-based best-result selection. Results are kept only
in user-host memory, become stale after storage changes, and can be cleared.
The duration includes engine startup, HTTPS probing, and cleanup; it is not ping.

Selected-profile organization supports local copies and manual-group moves.
Copies preserve protocol and transport/reliability options, receive a new ID and
start without a favorite mark. Subscription profiles can be copied to manual
groups; subscription members and managed destination groups cannot be moved.
Both actions require the reviewed storage snapshot and never start a connection.

Proxy profiles can store encrypted DNS and explicit split routing preferences for
whole-device mode. Ordered domain, domain-suffix, IP/subnet and executable-path
rules choose proxy, direct or reject; unmatched traffic stays proxied. Direct rules
are intentional exceptions to VPN routing, not an automatic LAN bypass. Custom
DoH/DoT uses a literal resolver IP, verified TLS identity and proxy detour. Upstream
server-name bootstrap uses the fixed encrypted infrastructure path, separately
from the custom tunneled resolver. Native AWG/WG with empty preferences retain imported DNS/AllowedIPs;
nonempty full-device policies use the shared TUN; local mode is rejected. A matching new
service build is required; an old strict service refuses unsupported fields.
Whole-device startup refreshes the interface snapshot before becoming ready so
executable-path rules apply to the first TUN connection.

Portable backups use password-derived authenticated encryption and include profile,
network, favorite, group and subscription settings. Restore previews a complete
library replacement and requires explicit confirmation; enabled subscription
schedules are restored only with a separate opt-in. Runtime/protection state and
machine-specific consent are excluded. Existing backup files are never overwritten.
Optional per-subscription schedules run only while the user app is open, default
off, wait their configured interval and prepare a review instead of applying it.
Tray profile selection uses the Go projection and never starts a connection.

Shared-TUN proxy policies support up to eight additional encrypted resolvers and
64 ordered exact/suffix domain DNS rules. Resolver indices select verified-TLS
DoH/DoT endpoints routed through the proxy; the default resolver remains final.
An opt-in LAN exception sends only RFC1918 IPv4 and IPv6 ULA directly after
explicit user rules. It defaults off, does not exclude OS routes, and never
removes persistent traffic protection. Native AWG/WG without network preferences remain configuration-driven.

Connection statistics come from the embedded engine and reset for each successful
connection attempt, including reconnect/fallback. Local-proxy payload includes
connection checks. Whole-device proxy totals count TUN payload and fast-flow
packet bytes; legacy native AWG/WG totals are transport counters. Rates are unavailable
until two samples exist. These are not provider billing counters. Older services
without statistics remain supported and show no fabricated totals. Diagnostics
classify typed failures by bounded stage/category; unsupported authentication
causes remain unknown rather than guessing from log text.

Windows user settings provide off-by-default auto-connect with separate trusted
and public network permissions (unknown is public), an explicit profile/mode,
five-second category debounce and at most three failed starts per category cycle.
Manual Disconnect inhibits automatic starts until off/on or a new user session.
The user app must be open. Startup registration is a separate current-user Run
entry using a validated local installation path; it never enables auto-connect.

System proxy activation is explicit and limited to the authenticated local mixed
listener. It preserves exact typed user values in a DPAPI journal before writes,
requires an acknowledged detached watchdog, and restores only unchanged values
OneTunnel owns. The watchdog handles owner death and three failed loopback checks.
PAC/auto-detection conflicts block activation. External changes are retained and
reported; machine WinHTTP is never modified. Closing the user host releases its
proxy lease without stopping the VPN service. Portable restore disables automatic
activation and excludes machine startup ownership.

Group checks report total check duration separately from successful HTTPS probe
latency. HTTPS latency excludes engine startup and cleanup and is not ICMP RTT.
Saved shared-TUN DNS/routing policies are validated, then left unapplied during
the local server check; infrastructure endpoint bootstrap stays encrypted.
Native WG/AWG group checks retain imported AllowedIPs and address families, reporting separate DNS exchange latency when eligible DNS responds. The duration may include the initial handshake. Missing eligible DNS remains configuration-only. DNS success does not certify public internet access or absence of leaks.

AWG/WireGuard with nonempty full-device network preferences use the shared TUN
and an in-process AWG userspace transport. This mode supports TCP/UDP, preserves
peer AllowedIPs and replaces imported DNS with the configured encrypted policy
resolver. Uncovered destinations fail closed; direct routing requires an explicit
rule. ICMP is unavailable in this mode. Empty preferences preserve legacy native
routing, DNS and ICMP. Resolver addresses must fit peer coverage/address families.
Native policy health checks an uncached encrypted DNS exchange rather than public
HTTPS; success does not certify arbitrary public access or absence of leaks.
Shared-native statistics use TUN ingress scope, without adding AWG wire counters.

The pinned core also supports bounded additional engine-native proxy targets,
inline domain/CIDR rule sets, compound process/network/port rules and AWG 3.1
options. The existing network rules editor accepts advanced JSON with `Rules`,
`Targets` and `RuleSets` and preserves it on reopen. Target sources are credentials:
ordinary profile responses redact them; the explicit editor includes them.
The core README documents the format, limits, SDK analysis and prediction APIs.
Remote rule-set fetching and secondary Xray/native OS tunnels are outside this
implementation. Route explanation is a read-only prediction, not live observation.
