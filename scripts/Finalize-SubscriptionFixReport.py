"""Sanitized evidence only; no subscription links, names, addresses or keys."""
import json
import re
import sys
from pathlib import Path

root=Path(__file__).resolve().parent.parent
private=Path(sys.argv[1]).resolve()
read=lambda p:p.read_text(encoding='utf-8-sig')
artifact=lambda name:read(root/'artifacts'/name)
initial=json.loads(read(private/'fresh-probe-results.json'))
final=json.loads(read(private/'saved-profile-verification.json'))
applied=json.loads(read(private/'applied-refresh.json'))
assert len(initial)==32
assert len(final)==3 and all(r['Https'] and not r['FullDevice'] and not r['KillSwitch'] for r in final)
assert applied['UnrelatedProfilesPreserved']==2 and applied['CollapsedGroupsPreserved'] and applied['EncryptedBackupCreated']
assert 'ALL 123 CHECKS PASSED' in artifact('direct-checks-0.9.1.txt')
assert 'ALL 116 CHECKS PASSED' in artifact('go-local-traffic-0.9.1.txt')
assert artifact('ui-checks-0.9.1.txt').count('PASS UI:')==30
assert artifact('service-ui-0.9.1.txt').count('PASS SERVICE UI:')==3
go=artifact('go-tests-0.9.1.txt')
assert len(re.findall(r'^--- PASS: Test',go,re.M))==21 and '--- FAIL:' not in go
assert '126 C# parity configurations passed' in go
assert 'PASS go vet' in artifact('go-vet-0.9.1.txt')
auto=artifact('auto-refresh-connections-0.9.1.txt')
assert auto.count('current_subscription_loaded=True')==3 and auto.count('HTTPS=True')==3
summary=[]
for protocol in ['VLESS','Hysteria2','Shadowsocks']:
    rows=[r for r in initial if r['Protocol']==protocol]
    summary.append({'Protocol':protocol,'Transport':'tcp' if protocol=='VLESS' else 'native','InitialTested':len(rows),'InitialHttpsPassed':sum(r['Https'] for r in rows)})
report={
    'Version':'0.9.1','Date':'2026-10-22',
    'Cause':'Saved subscription endpoints and REALITY SNI/short IDs differed from current provider data. Reimport previously appended changed nodes rather than refreshing the group.',
    'Fixes':['Refresh the selected URL subscription group, preserve matching profile IDs and unrelated groups/native profiles, remove retired subscription nodes.',
             'Store the subscription source only inside the existing DPAPI-encrypted profile document; refresh it before connection. If unavailable, keep the cached profile.',
             'Validate HTTPS using bounded fallback across independent targets; preserve TLS certificate verification and reject captive/login content.'],
    'InitialSweepCoreVersion':'0.9','InitialSweep':summary,
    'FinalCoreVersion':'0.9.1','FinalAutoRefreshConnections':final,
    'FinalProfileCount':applied['After'],'UnrelatedNativeProfilesPreserved':2,
    'CollapsedGroupsPreserved':True,'EncryptedBackupsCreated':True,
    'GoTests':21,'GoVetPassed':True,'ParityConfigurations':126,'CSharpRegressionChecks':123,
    'LocalServiceChecks':116,'UiChecks':30,'UiLifecycleChecks':3,
    'Safety':{'TestsUsedLocalProxyOnly':True,'FullDeviceEnabledDuringThisInvestigation':False,'KillSwitchEnabledDuringThisInvestigation':False,'WindowsDnsRoutesAndClockNotChanged':True},
    'Limits':['Two initially failing VLESS entries had AAAA records but no IPv4 addresses; physical IPv6 was unavailable.',
              'Another initially failing VLESS entry returned NXDOMAIN from the checked public DNS and an unreachable cached IPv6 address from Windows.',
              'Repeated checks also encountered transient timeouts. The final fresh-before-connect representatives for all three protocols passed; this is not a guarantee that every provider node remains available.',
              'No new full-device fault or leak test was run in this iteration. The 0.9 report records earlier coverage.',
              'An optional second-resolver cross-check was rejected by automatic access review and was not performed.']
}
(root/'artifacts'/'subscription-check-0.9.1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Subscription fix report verified; private subscription data excluded.')
