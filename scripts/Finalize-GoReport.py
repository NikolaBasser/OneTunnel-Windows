"""Export a non-secret report of the first Go migration stage."""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
run = Path(sys.argv[1]).resolve()
rows = json.loads((run / 'kill-switch-results.json').read_text(encoding='utf-8-sig'))
capture = json.loads((run / 'capture-summary.json').read_text())
required = ['GoCore', 'ConnectedHttps', 'ClientReattachment', 'ExistingPhysicalConnectionBlocked', 'EngineFailureBlockedTcp', 'AdapterFailureBlockedTcp', 'GoServiceSurvivedAdapterCrash', 'AdapterRecoveryRestoredNetwork', 'AppCrashBlockedTcp', 'PersistentFiltersAfterCrash', 'RecoveryRestoredNetwork']
assert len(rows) == 8 and len(capture['Checks']) == 24
assert all(r['Failure'] is None and all(r[k] for k in required) for r in rows)
canaries = ['physical_dns53_queries', 'physical_dns_canary_queries', 'direct_ipv4_canary443_packets', 'direct_ipv6_canary443_packets', 'direct_ipv6_linklocal_canary_packets', 'PhysicalDnsQueryReplyPairs']
assert all(not c.get(k, 0) for c in capture['Checks'] for k in canaries)
def routes(text):
    text = text[text.index('IPv4'):].split('\nInterface:')[0]
    return {line.strip() for line in text.splitlines() if re.match(r'^\s*(?:\d+\s+\d+\s+[a-fA-F0-9:]+/|\d+\.\d+\.\d+\.\d+\s+)', line)}
def dns(text):
    return {line.split(';')[0]: line.split('; DNS=')[1].split('; addresses=')[0] for line in text.splitlines() if line.startswith('Interface:') and '; DNS=' in line and not line.startswith('Interface: OneTunnel')}
before = (run / 'before.txt').read_text(encoding='utf-8-sig')
after = (run / 'after.txt').read_text(encoding='utf-8-sig')
assert routes(before) == routes(after) and dns(before) == dns(after)
tests = (root / 'artifacts/tests-0.7.txt').read_text(encoding='utf-8-sig')
local = (root / 'artifacts/go-local-traffic-0.7.txt').read_text(encoding='utf-8-sig')
assert 'ALL 116 CHECKS PASSED' in tests and 'ALL 109 CHECKS PASSED' in local
go_tests = (root / 'artifacts/go-tests-0.7.txt').read_text(encoding='utf-8-sig')
assert go_tests.count('--- PASS:') == 11 and '--- FAIL:' not in go_tests
assert (root / 'artifacts/service-ui-0.7.txt').read_text(encoding='utf-8-sig').count('PASS SERVICE UI:') == 3
assert (root / 'artifacts/ui-tests-0.7.txt').read_text(encoding='utf-8-sig').count('PASS UI:') == 24
assert 'elevated=False' in (root / 'artifacts/service-ipc-0.7.txt').read_text(encoding='utf-8-sig')
result = []
for row in rows:
    item = {k: row[k] for k in ['Protocol', 'Transport', 'LocalFixture', 'SubscriptionRepresentativeUnavailable', 'PreflightCandidatesTried', 'ConnectionAttempts', 'HttpsProbeAttempts', 'Ipv6Baseline', 'EngineFailureIpv6', 'AdapterFailureIpv6', 'AppCrashIpv6'] + required}
    item['ServiceCrashBlockedTcp'] = item.pop('AppCrashBlockedTcp')
    item['ServiceCrashIpv6'] = item.pop('AppCrashIpv6')
    result.append(item)
report = {'Version': '0.7', 'Stage': 'Go SCM/API/session manager and supervisor; transitional C# Windows/protocol adapter retained', 'Date': '2026-10-21', 'Results': result, 'OutageWindows': 24, 'CapturePackets': capture['Packets'], 'CaptureEventLoss': 0, 'CaptureLossCountersReviewed': True, 'DirectTcpDnsCanaryPacketsObserved': 0, 'RoutesAndDnsRestored': True, 'NonAdminServiceApiVerified': True, 'GoTests': 11, 'GoVetPassed': True, 'CSharpRegressionChecks': 116, 'LocalGoServiceSuiteChecks': 109, 'UiChecks': 24, 'UiLifecycleChecks': 3, 'Limits': ['The full core is not yet Go: protocol configuration, native networking and WFP remain in the private C# adapter.', 'All 24 saved VLESS TCP profiles failed preliminary remote HTTPS tests; the first also failed with the previous direct C# core. VLESS TCP is verified on a local fixture, not the subscription servers.', 'Remote Hysteria2, Shadowsocks, VLESS WS/XHTTP, WireGuard and AmneziaWG passed. SOCKS5 used a local fixture.', 'Global physical IPv6 baseline unavailable; denied IPv6 attempts do not constitute full external IPv6 coverage.', 'Reboot, sleep/resume and network changes were not tested.', 'Race detector not run without a C toolchain; explicit concurrent lifecycle tests passed.', 'Suite counts overlap and must not be added as unique checks. Results apply to selected profiles and capture windows.']}
(root / 'artifacts/go-core-check-0.7.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('Public Go migration report prepared; routes and DNS restored.')
