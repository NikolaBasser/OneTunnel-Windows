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
The pinned-core integration job also runs isolated WPF workspace checks with
synthetic profiles. These checks do not connect a tunnel or modify the installed service.
An availability result is not a blanket guarantee about a remote server, and native
WireGuard/AmneziaWG group checks validate configuration rather than establishing a tunnel.

[Security policy](.github/SECURITY.md) ·
[Third-party notices](THIRD-PARTY-NOTICES.txt) ·
[Original dependency notices](third-party/notices/)

Upstream components retain their respective licenses. No additional project-wide
license grant is declared by this README.

Group check results can be sorted by full check duration and filtered by outcome.
An explicit best-result action selects a successful network check without connecting.
WireGuard/AmneziaWG configuration-only checks are excluded. Results are kept only
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
from the custom tunneled resolver. Native AWG/WG retain imported DNS/AllowedIPs;
new policies and local proxy mode are incompatible and rejected. A matching new
service build is required; an old strict service refuses unsupported fields.

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
removes persistent traffic protection. Native AWG/WG remain configuration-driven.

Connection statistics come from the embedded engine and reset for each successful
connection attempt, including reconnect/fallback. Local-proxy payload includes
connection checks. Whole-device proxy totals count TUN payload and fast-flow
packet bytes; native AWG/WG totals are transport counters. Rates are unavailable
until two samples exist. These are not provider billing counters. Older services
without statistics remain supported and show no fabricated totals. Diagnostics
classify typed failures by bounded stage/category; unsupported authentication
causes remain unknown rather than guessing from log text.
