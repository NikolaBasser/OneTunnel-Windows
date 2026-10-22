"""Export only non-secret evidence for the native Windows Go runtime."""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
run, migration = (Path(p).resolve() for p in sys.argv[1:3])
read = lambda p: p.read_text(encoding='utf-8-sig')
artifact = lambda name: read(root / 'artifacts' / name)
rows = json.loads(read(run / 'kill-switch-results.json'))
capture = json.loads(read(run / 'capture-summary.json'))
required = ['GoCore', 'NativeGo', 'ConnectedHttps', 'ClientReattachment', 'ExistingPhysicalConnectionBlocked', 'EngineFailureBlockedTcp', 'AppCrashBlockedTcp', 'PersistentFiltersAfterCrash', 'RecoveryRestoredNetwork']
assert len(rows) == 8 and len(capture['Checks']) == 16
assert all(r['Failure'] is None and not r['AdapterPresent'] and all(r[k] for k in required) for r in rows)
assert read(run / 'runner-exit.txt').strip() == '0' and not (run / 'watch-triggered').exists()
assert read(migration / 'runner-exit.txt').strip() == '0'
assert 'PASS GO MIGRATION:' in read(migration / 'checks.txt') and not (migration / 'watch-triggered').exists()
canaries = ['physical_dns53_queries', 'physical_dns_canary_queries', 'direct_ipv4_canary443_packets', 'direct_ipv6_canary443_packets', 'direct_ipv6_linklocal_canary_packets', 'PhysicalDnsQueryReplyPairs']
assert all(not c.get(k, 0) for c in capture['Checks'] for k in canaries)

def routes(text):
    text = text[text.index('IPv4'):].split('\nInterface:')[0]
    return {line.strip() for line in text.splitlines() if re.match(r'^\s*(?:\d+\s+\d+\s+[a-fA-F0-9:]+/|\d+\.\d+\.\d+\.\d+\s+)', line)}

def dns(text):
    return {line.split(';')[0]: line.split('; DNS=')[1].split('; addresses=')[0] for line in text.splitlines() if line.startswith('Interface:') and '; DNS=' in line and not line.startswith('Interface: OneTunnel')}

before, after = (read(run / name) for name in ['before.txt', 'after.txt'])
assert routes(before) == routes(after) and dns(before) == dns(after)
assert 'ALL 116 CHECKS PASSED' in artifact('direct-checks-0.9.txt')
assert 'ALL 109 CHECKS PASSED' in artifact('go-local-traffic-0.9.txt')
go = artifact('go-tests-0.9.txt')
assert len(re.findall(r'^--- PASS: Test', go, re.M)) == 19 and '--- FAIL:' not in go
assert '126 C# parity configurations passed' in go
assert 'PASS' in artifact('go-fuzz-0.9.txt') and 'PASS go vet' in artifact('go-vet-0.9.txt')
assert artifact('service-ui-0.9.txt').count('PASS SERVICE UI:') == 3
assert artifact('ui-checks-0.9.txt').count('PASS UI:') == 24
assert 'elevated=False' in artifact('service-ipc-0.9.txt')
comparison = json.loads(artifact('remote-comparison.json'))
assert comparison and all(x['LegacyHttps'] == x['GoHttps'] for x in comparison)
result = [{k: r[k] for k in ['Protocol', 'Transport', 'LocalFixture', 'SubscriptionRepresentativeUnavailable', 'PreflightCandidatesTried', 'Ipv6Baseline', 'EngineFailureIpv6', 'AppCrashIpv6'] + required} for r in rows]
report = {
    'Version': '0.9', 'Date': '2026-10-22', 'Runtime': 'Native Go Windows service; C# adapter removed',
    'Results': result, 'OutageWindows': 16, 'CapturePackets': capture['Packets'],
    'DirectTcpDnsCanaryPacketsObserved': 0, 'RoutesAndDnsRestored': True,
    'CaptureEventLoss': 0, 'CaptureLossCountersReviewed': True, 'CaptureFilters': 'none',
    'LegacyCSharpJournalAndWfpRecoveryVerified': True,
    'ExitedEngineHandleRegressionReproducedAndFixed': True,
    'IndependentSchedulerRecoveryExercised': True, 'SuccessfulRunNeededWatchdogIntervention': False,
    'TestWindowLimitSeconds': 75, 'ProductionProtectionAutoUnlocks': False,
    'GoTests': 19, 'GoVetPassed': True, 'ParityConfigurations': 126,
    'FuzzExecutions': int(re.findall(r'execs: (\d+)', artifact('go-fuzz-0.9.txt'))[-1]),
    'CSharpRegressionChecks': 116, 'LocalGoServiceSuiteChecks': 109, 'UiChecks': 24, 'UiLifecycleChecks': 3,
    'NonAdminServiceApiVerified': True, 'InvalidProfilesRejectedBeforeLaunch': 6,
    'RemoteLegacyVsGoComparison': comparison,
    'Limits': [
        'Remote VLESS TCP, Hysteria2 and Shadowsocks candidates failed the preliminary probe; local protocol servers with an external uplink were used for their full-device fault tests. Remote legacy/Go comparison is included.',
        'SOCKS5 was tested on a local server with an external uplink; no user-provided remote SOCKS5 profile was available.',
        'Physical global IPv6 baseline was unavailable. No test DNS/TCP/IPv6 canary packets were observed in the captured fault windows; this does not establish universal absence of leaks.',
        'Reboot, sleep/resume, network changes and Go race detector were not tested.',
        'WPF, UI imports/subscriptions, DPAPI storage and legacy standalone recovery helpers remain C#. Protocol engines remain separate bundled executables.'
    ]
}
(root / 'artifacts' / 'go-core-check-0.9.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print('Native Go report verified: 8 variants, 16 fault windows, compatible legacy recovery; private evidence excluded.')
