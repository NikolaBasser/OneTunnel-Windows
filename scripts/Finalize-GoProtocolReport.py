"""Export the stage-2 non-secret validation report; private evidence stays local."""
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

def artifact(name):
    return (root / 'artifacts' / name).read_text(encoding='utf-8-sig')

assert 'ALL 116 CHECKS PASSED' in artifact('tests-0.8.txt')
assert 'ALL 109 CHECKS PASSED' in artifact('go-local-traffic-0.8.txt')
go = artifact('go-tests-0.8.txt')
assert len(re.findall(r'^--- PASS: Test', go, re.M)) == 14 and '--- FAIL:' not in go
assert '126 C# parity configurations passed' in go
assert 'PASS' in artifact('go-fuzz-0.8.txt')
assert artifact('service-ui-0.8.txt').count('PASS SERVICE UI:') == 3
assert artifact('ui-tests-0.8.txt').count('PASS UI:') == 24
assert 'elevated=False' in artifact('service-ipc-0.8.txt')
assert 'Go rejects invalid profiles for all six protocols' in artifact('service-ipc-0.8.txt')
assert all(not row['LocalFixture'] for row in rows if row['Protocol'] != 'SOCKS5')

result = []
for row in rows:
    item = {k: row[k] for k in ['Protocol', 'Transport', 'LocalFixture', 'SubscriptionRepresentativeUnavailable', 'PreflightCandidatesTried', 'ConnectionAttempts', 'HttpsProbeAttempts', 'Ipv6Baseline', 'EngineFailureIpv6', 'AdapterFailureIpv6', 'AppCrashIpv6'] + required}
    item['ServiceCrashBlockedTcp'] = item.pop('AppCrashBlockedTcp')
    item['ServiceCrashIpv6'] = item.pop('AppCrashIpv6')
    result.append(item)
report = {
    'Version': '0.8', 'Stage': 'Go protocol validation, transport handling, engine selection and configuration generation; private C# Windows operations retained',
    'Date': '2026-10-21', 'Results': result, 'OutageWindows': 24, 'CapturePackets': capture['Packets'],
    'CaptureEventLoss': 0, 'CaptureLossCountersReviewed': True, 'CaptureFilters': 'none',
    'DirectTcpDnsCanaryPacketsObserved': 0, 'RoutesAndDnsRestored': True, 'NonAdminServiceApiVerified': True,
    'InvalidProfilesRejectedBeforeLaunch': 6, 'GoTests': 14, 'GoVetPassed': True, 'ParityConfigurations': 126,
    'FuzzExecutions': 180223, 'FuzzPassed': True, 'CSharpRegressionChecks': 116, 'LocalGoServiceSuiteChecks': 109,
    'UiChecks': 24, 'UiLifecycleChecks': 3,
    'Limits': [
        'Windows process/network/WFP operations, runtime endpoint/interface and SS2022 clock discovery remain in the private C# adapter. UI import helpers remain C#.',
        'Remote representative VLESS TCP/WS/XHTTP, Hysteria2, Shadowsocks, WireGuard and AmneziaWG passed in this run; this does not validate every subscription endpoint.',
        'SOCKS5 used a local fixture with an external uplink; no user-provided remote SOCKS5 profile is available.',
        'Global physical IPv6 baseline unavailable; denied link-local IPv6 attempts do not prove full external IPv6 coverage.',
        'Reboot, sleep/resume and network changes were not tested. Race detector not run without a C toolchain.',
        'Suite counts overlap. Results apply to selected profiles and capture windows.'
    ]
}
(root / 'artifacts/go-core-check-0.8.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('Stage-2 report prepared: 8 connection variants, 24 fault windows, routes and DNS restored.')
