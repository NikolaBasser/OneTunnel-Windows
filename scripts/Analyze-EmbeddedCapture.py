"""Local, credential-free observations for guarded embedded-engine captures."""
import collections
import json
import struct
import sys
from pathlib import Path

root = Path(sys.argv[1]).resolve()
reports = []
for capture in sorted(root.glob('network-*.pcapng')):
    index = int(capture.stem.split('-')[1])
    mac = bytes.fromhex(json.loads((root / f'client-input-{index}.json').read_text())['MAC'].replace(':', ''))
    phases = {}
    for mode in ('baseline', 'connected', 'broken', 'restored', 'crashed'):
        path = root / f'client-{index}-{mode}.json'
        if path.exists():
            item = json.loads(path.read_text())
            phases[mode] = (item, collections.Counter())
    data = capture.read_bytes()
    offset = 0
    total = 0
    while offset + 12 <= len(data):
        kind, length = struct.unpack_from('<II', data, offset)
        if length < 12 or offset + length > len(data):
            raise ValueError('Invalid capture block')
        if kind == 6:
            _, high, low, captured, _ = struct.unpack_from('<IIIII', data, offset + 8)
            timestamp = ((high << 32) | low) / 1000
            packet = data[offset + 28:offset + 28 + captured]
            total += 1
            for mode, (item, counters) in phases.items():
                if not item['BeginMilliseconds'] <= timestamp <= item['EndMilliseconds']:
                    continue
                if len(packet) < 34 or packet[6:12] != mac:
                    continue
                eth = struct.unpack_from('>H', packet, 12)[0]
                if eth == 0x0800:
                    header, proto = 14 + (packet[14] & 15) * 4, packet[23]
                elif eth == 0x86DD and len(packet) >= 54:
                    header, proto = 54, packet[20]
                else:
                    continue
                counters['PhysicalOutgoingPackets'] += 1
                if proto not in (6, 17) or len(packet) < header + 8:
                    continue
                source, dest = struct.unpack_from('>HH', packet, header)
                base = {'baseline': 60010, 'connected': 60020, 'broken': 60030, 'restored': 60040, 'crashed': 60000}[mode]
                if source in (base+1, base+2, base+3, base+4):
                    counters['DirectCanaryPackets'] += 1
                if dest == 53:
                    counters['PlaintextDNSPackets'] += 1
                    if b'onetunnel-' in packet:
                        counters['SystemDNSCanaryPackets'] += 1
        offset += length
    reports.append({'Index': index, 'CapturedPackets': total, 'Phases': {
        mode: {'HTTPS': item['HTTPS'], 'GlobalIPv6Available': item['GlobalIPv6Available'], **counts}
        for mode, (item, counts) in phases.items()},
        'Conclusion': 'Observations require capture coverage, filters and lost-event review.'})
report = {'Checks': reports}
(root / 'embedded-capture-summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
