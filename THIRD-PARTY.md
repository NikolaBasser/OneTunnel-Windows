# Third-party components

OneTunnel embeds the pinned Xray, sing-box and AmneziaWG libraries into its Go service. See EMBEDDED-ENGINES.md and the generated dependency notice inventory. No affiliation with the upstream projects is implied.

| Component | Pinned version | Official source / license |
|---|---|---|
| sing-box | 1.14.3 | https://github.com/SagerNet/sing-box/tree/v1.14.3 · GPL-3.0-or-later, see upstream LICENSE and applicable notices |
| Xray-core | 26.3.27 | https://github.com/XTLS/Xray-core/tree/v26.3.27 · MPL-2.0; original LICENSE shipped as engines/LICENSE.xray.txt |
| Twemoji graphics | 17.0.3 | Copyright Twitter, Inc. and other contributors; https://github.com/jdecked/twemoji · CC-BY-4.0 graphics, original LICENSE-GRAPHICS shipped as assets/LICENSE.twemoji.txt; unmodified local PNG assets, provenance in assets/emoji-provenance.json |
| AmneziaWG for Windows, awg | 3.1.0 | https://github.com/amnezia-vpn/amneziawg-windows-client/tree/3.1.0 · see COPYING and per-file licenses; dependencies listed in upstream go.mod |
| Embedded AmneziaWG Go and Windows IP helpers | 3.1.20260814 | https://github.com/amnezia-vpn/amneziawg-go/tree/v3.1.20260814 and https://github.com/amnezia-vpn/amneziawg-windows/tree/v3.1.20260814 · preserve module and per-file notices; only winipcfg from the Windows module is linked |
| Wintun | bundled in AmneziaWG MSI | https://www.wintun.net/ · https://git.zx2c4.com/wintun/about/ · upstream binary distribution license |
| Go runtime | 1.27.3 | https://go.dev/dl/ · BSD-3-Clause, service/LICENSE.go.txt |
| uniseg | 0.4.7 | https://github.com/rivo/uniseg/tree/v0.4.7 · MIT, assets/LICENSE.uniseg.txt; Go profile-name grapheme validation |
| go-winio | 0.6.2 | https://github.com/microsoft/go-winio/tree/v0.6.2 · MIT, service/LICENSE.go-winio.txt |
| golang.org/x/sys | 0.49.0 | https://pkg.go.dev/golang.org/x/sys@v0.49.0 · BSD-3-Clause, service/LICENSE.x-sys.txt |
| golang.org/x/net | 0.61.0 | https://pkg.go.dev/golang.org/x/net@v0.61.0 · BSD-3-Clause, service/LICENSE.x-net.txt |
| golang.org/x/text | 0.43.0 | https://pkg.go.dev/golang.org/x/text@v0.43.0 · BSD-3-Clause, service/LICENSE.x-text.txt |

Exact downloaded package URLs, package SHA256 and verification method are recorded in engines/provenance.json. The corresponding-source repositories, build instructions and original releases are available at the links above. Preserve upstream notices. Before external distribution, provide the exact corresponding source and required license/notice files for all shipped components and review their terms. This working folder is a local development deliverable.
