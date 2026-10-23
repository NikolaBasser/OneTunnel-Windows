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

The interface runs in the user session. A separate authenticated Go service manages
engines and system networking. Closing the window leaves the service connection active.
Ordinary minimization retains the taskbar. The separate minimize-to-tray button
hides the window; restore it from the tray or press Ctrl+Shift+O. The shortcut is
reserved only while hidden. If Windows cannot register it, the window stays visible
and reports the conflict. An Explorer restart restores an explicitly hidden window.
Status notifications are off by default and can be enabled in the window or tray;
the choice resets when the application exits.
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
