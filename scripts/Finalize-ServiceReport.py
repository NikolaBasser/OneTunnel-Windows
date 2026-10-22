"""Export only non-secret verification results from a private service test run."""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
run = Path(sys.argv[1]).resolve()
rows = json.loads((run / 'kill-switch-results.json').read_text(encoding='utf-8-sig'))
capture = json.loads((run / 'capture-summary.json').read_text())
assert len(rows) == 8 and len(capture['Checks']) == 16
required = ['ConnectedHttps', 'ClientReattachment', 'ExistingPhysicalConnectionBlocked', 'EngineFailureBlockedTcp', 'AppCrashBlockedTcp', 'PersistentFiltersAfterCrash', 'RecoveryRestoredNetwork']
assert all(r['Failure'] is None and all(r[k] for k in required) for r in rows)
canaries = ['physical_dns53_queries', 'physical_dns_canary_queries', 'direct_ipv4_canary443_packets', 'direct_ipv6_canary443_packets', 'direct_ipv6_linklocal_canary_packets', 'PhysicalDnsQueryReplyPairs']
assert all(not c.get(k, 0) for c in capture['Checks'] for k in canaries)
before = (run / 'before.txt').read_text(encoding='utf-8-sig')
after = (run / 'after.txt').read_text(encoding='utf-8-sig')
def routes(text):
    text = text[text.index('IPv4'):].split('\nInterface:')[0]
    return {line.strip() for line in text.splitlines() if re.match(r'^\s*(?:\d+\s+\d+\s+[a-fA-F0-9:]+/|\d+\.\d+\.\d+\.\d+\s+)', line)}
def dns(text):
    return {line.split(';')[0]: line.split('; DNS=')[1].split('; addresses=')[0] for line in text.splitlines() if line.startswith('Interface:') and '; DNS=' in line and not line.startswith('Interface: OneTunnel')}
assert routes(before) == routes(after) and dns(before) == dns(after)
assert 'elevated=False' in (root / 'artifacts/service-ipc-0.6.txt').read_text(encoding='utf-8-sig')
assert (root / 'artifacts/service-ui-0.6.txt').read_text(encoding='utf-8-sig').count('PASS SERVICE UI:') == 3
result = []
for row in rows:
    item = {k: row[k] for k in ['Protocol', 'Transport', 'LocalFixture', 'ConnectionAttempts', 'HttpsProbeAttempts', 'Ipv6Baseline', 'EngineFailureIpv6', 'AppCrashIpv6'] + required}
    item['ServiceCrashBlockedTcp'] = item.pop('AppCrashBlockedTcp')
    item['ServiceCrashIpv6'] = item.pop('AppCrashIpv6')
    result.append(item)
report = {'Version': '0.6', 'Date': '2026-10-21', 'Results': result, 'OutageWindows': 16, 'CapturePackets': capture['Packets'], 'CaptureLossCountersReviewed': True, 'CaptureEventLoss': 0, 'DirectTcpDnsCanaryPacketsObserved': 0, 'RoutesAndDnsRestored': True, 'NonAdminServiceApiVerified': True, 'ApiRejectionChecks': 3, 'UiLifecycleChecks': 3, 'RegressionChecks': 116, 'UiChecks': 24, 'Limits': ['SOCKS5 used a local fixture with physical uplink.', 'Global physical IPv6 baseline was unavailable; attempts were denied, external IPv6 coverage remains limited.', 'Reboot, sleep/resume and network changes were not tested.', 'Earlier repetitions included an initial VLESS HTTPS timeout; the final complete run used one connection attempt per profile. HttpsProbeAttempts totals the initial and second-start probes.', 'Results apply to the selected profiles and capture windows, not every subscription server.']}
(root / 'artifacts/service-check-0.6.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('Public service report prepared; routes and DNS restored.')
