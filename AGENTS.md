# Project conventions

- General documentation describes the product, current architecture and supported behavior. It does not contain conversation history, progress reports, instructions addressed to a particular user, or a version-by-version work diary.
- Describe version changes, verification and material limitations in commit messages. Keep README focused on the project; do not add a changelog unless explicitly requested.
- Architectural reference: https://github.com/bimeron/bimeron-core . Consult its current source/docs when relevant. Preserve OneTunnel's actual platform support and dependency licenses; do not copy unsupported capabilities or licensing claims.
- The GitHub repository is private and belongs to NikolaBasser. Verify the authenticated account before remote mutations. Never publish credentials, subscription sources, user profiles, captures or private diagnostics.
- VPN system tests require independent bounded recovery before routes, DNS or kill-switch state are changed. Keep test recovery separate from production fail-closed behavior.
