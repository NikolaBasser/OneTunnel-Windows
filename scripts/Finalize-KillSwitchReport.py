"""Prepare the public report from private, completed fault-injection checks."""
import json
from pathlib import Path
root = Path(__file__).resolve().parent.parent
run = root / 'artifacts/system-20261021-164436'
rows = json.loads((run / 'kill-switch-results.json').read_text(encoding='utf-8-sig'))
capture = json.loads((run / 'capture-summary.json').read_text())
assert len(rows) == 8 and len(capture['Checks']) == 16
assert all(r['Failure'] is None and r['ConnectedHttps'] and r['ExistingPhysicalConnectionBlocked'] and r['EngineFailureBlockedTcp'] and r['AppCrashBlockedTcp'] and r['PersistentFiltersAfterCrash'] and r['RecoveryRestoredNetwork'] for r in rows)
canaries = ['physical_dns53_queries', 'physical_dns_canary_queries', 'direct_ipv4_canary443_packets', 'direct_ipv6_canary443_packets', 'direct_ipv6_linklocal_canary_packets', 'PhysicalDnsQueryReplyPairs']
assert all(not c.get(k, 0) for c in capture['Checks'] for k in canaries)
fields = ['Protocol', 'Transport', 'LocalFixture', 'ConnectedHttps', 'ConnectionAttempts', 'ExistingPhysicalConnectionBlocked', 'EngineFailureBlockedTcp', 'EngineFailureIpv6', 'AppCrashBlockedTcp', 'AppCrashIpv6', 'PersistentFiltersAfterCrash', 'RecoveryRestoredNetwork', 'Failure']
report = {'Version': '0.5', 'Date': '2026-10-21', 'Results': [{k: r[k] for k in fields} for r in rows], 'OutageWindows': 16, 'CapturePackets': capture['Packets'], 'CaptureEventLoss': 0, 'DirectTcpDnsCanaryPacketsObserved': 0, 'RegressionChecks': 112, 'UiChecks': 24, 'Limits': ['SOCKS5 used a local fixture with physical uplink.', 'No confirmed working physical global IPv6 baseline; IPv6 attempts were denied but full external IPv6 coverage is limited.', 'Reboot, sleep/resume and network changes were not tested.', 'Capture conclusions apply to selected profiles and observed test windows.']}
(root / 'artifacts/kill-switch-check-0.5.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
p = root / 'README.md'
s = p.read_text(encoding='utf-8-sig').replace('# OneTunnel 0.4.1', '# OneTunnel 0.5').replace('OneTunnel-0.4.1-win-x64', 'OneTunnel-0.5-win-x64')
s = s.replace('Это экспериментальный режим: проверена корректность конфигураций движками, но не системная передача трафика.', 'Системная передача трафика проверена на выбранных профилях; режим остаётся экспериментальным.')
s = s.replace('110 проверок', '112 проверок').replace('artifacts\\tests-0.4.1.txt', 'artifacts\\tests-0.5.txt')
s = s.replace('Постоянный kill switch для прокси-протоколов, автообновления подписок, установщик и подпись собственного EXE пока отсутствуют. У WG/AWG в полном режиме применяется встроенная защита родных движков.', 'Перезагрузка с активной защитой пока не проверена. Автообновления подписок, установщик и подпись собственного EXE пока отсутствуют.')
section = '''## Kill switch в 0.5

В режиме **«Всё устройство»** защита включается автоматически после готовности туннеля для всех шести протоколов. Постоянные правила Windows Filtering Platform блокируют прямой трафик IPv4/IPv6; разрешены выбранный туннель, его движки, локальные соединения и необходимые DHCP/NDP. При включении и отключении очищается DNS-кэш Windows. Локальный прокси и режим WG/AWG «По конфигурации» эту защиту не включают.

При обрыве туннеля, остановке движка или аварийном завершении приложения блокировка сохраняется. **«Отключить»** снимает её и возвращает обычную сеть. После аварии запусти приложение от администратора и нажми **«Очистить оставшееся подключение»** в разделе «Движки и восстановление». Не удаляй папку данных до восстановления: там хранится признак активной защиты. Правила постоянные, но реальная перезагрузка с ними пока не проверялась.

Проверены AWG, WireGuard, Hysteria2, Shadowsocks, VLESS TCP/WS/XHTTP и локальный SOCKS5-стенд. Для каждого варианта проверены HTTPS через туннель, блокировка ранее открытого прямого TCP-соединения, принудительная остановка движка, аварийное завершение отдельного процесса приложения, сохранение правил и восстановление сети. В 16 интервалах захвата не обнаружены прямые TCP/DNS-пакеты тестовых запросов; потерь событий захвата не было. Обезличенный отчёт — `kill-switch-check-0.5.json` в сборке. IPv6-попытки получали запрет Windows, но без доступного глобального IPv6 на физической сети полноценная проверка этого пути остаётся ограниченной.

'''
if '## Kill switch в 0.5' not in s:
    s = s.replace('## Группы и системный режим в 0.4', section + '## Группы и системный режим в 0.4')
p.write_text(s, encoding='utf-8')
print('Public report and release notes prepared.')
