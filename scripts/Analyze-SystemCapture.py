"""Summarize canary traffic on the physical NIC without printing packet contents."""
import collections
import ipaddress
import json
import re
import struct
import sys
from pathlib import Path

root = Path(sys.argv[1]).resolve()
before = (root / "before.txt").read_text(encoding="utf-8-sig")
mac = None
for line in before.splitlines():
    match = re.search(r"\.{3}((?:[0-9a-f]{2}[ -]){5}[0-9a-f]{2})", line, re.I)
    if match and not any(word in line.lower() for word in ("virtual", "hyper-v", "software", "loopback", "tunnel")):
        mac = bytes.fromhex(re.sub(r"[ -]", "", match.group(1)))
        break
if mac is None:
    raise SystemExit("Physical adapter MAC could not be identified; capture review required.")
kill_switch = (root / "kill-switch-results.json").exists()
results = json.loads((root / ("kill-switch-results.json" if kill_switch else "results.json")).read_text(encoding="utf-8-sig"))
windows, canary_ports, ipv6_ports = {}, {}, {}
for item in results:
    phases = item.get("BlockedWindows", []) if kill_switch else [item]
    for phase_index, phase in enumerate(phases):
        if phase.get("BeginMilliseconds", 0) and phase.get("EndMilliseconds", 0):
            index = f'{item["Index"]}.{phase_index + 1}' if kill_switch else item["Index"]
            windows[index] = (phase["BeginMilliseconds"] / 1000, phase["EndMilliseconds"] / 1000)
            canary_ports[index] = phase.get("TcpSourcePort")
            ipv6_ports[index] = phase.get("Ipv6TcpSourcePort")
stats = {index: collections.Counter() for index in windows}
queries, replies = {i: set() for i in windows}, {i: set() for i in windows}
data = (root / "network.pcapng").read_bytes()
offset, packets = 0, 0
while offset + 12 <= len(data):
    kind, length = struct.unpack_from("<II", data, offset)
    if length < 12 or offset + length > len(data):
        raise SystemExit("Invalid capture block.")
    if kind == 6:
        _, high, low, captured, _ = struct.unpack_from("<IIIII", data, offset + 8)
        timestamp = ((high << 32) | low) / 1_000_000
        packet = data[offset + 28:offset + 28 + captured]
        packets += 1
        for index, (start, end) in windows.items():
            if not start <= timestamp <= end or len(packet) < 34:
                continue
            outgoing = packet[6:12] == mac
            incoming = packet[:6] == mac
            if not outgoing and not incoming:
                continue
            eth = struct.unpack_from(">H", packet, 12)[0]
            if eth == 0x0800:
                header, family, protocol = 14 + (packet[14] & 15) * 4, 4, packet[23]
                source, destination = packet[26:30], packet[30:34]
            elif eth == 0x86DD and len(packet) >= 54:
                header, family, protocol = 54, 6, packet[20]
                source, destination = packet[22:38], packet[38:54]
            else:
                continue
            if outgoing:
                stats[index][f"physical_ipv{family}_packets"] += 1
                if family == 6 and ipaddress.ip_address(destination).is_global and not ipaddress.ip_address(destination).is_multicast:
                    stats[index]["physical_global_ipv6_packets"] += 1
            if protocol not in (6, 17) or len(packet) < header + 8:
                continue
            source_port, destination_port = struct.unpack_from(">HH", packet, header)
            if outgoing and family == 6 and protocol == 6 and ipv6_ports[index] and source_port == ipv6_ports[index]:
                stats[index]["direct_ipv6_linklocal_canary_packets"] += 1
            if outgoing and destination_port == 443 and (canary_ports[index] is None or source_port == canary_ports[index]) and str(ipaddress.ip_address(destination)) in ("1.1.1.1", "2606:4700:4700::1111"):
                stats[index][f"direct_ipv{family}_canary443_packets"] += 1
            if source_port != 53 and destination_port != 53:
                continue
            dns_offset = header + 8 if protocol == 17 else header + (packet[header + 12] >> 4) * 4 + 2
            if len(packet) < dns_offset + 12:
                continue
            identifier, flags = struct.unpack_from(">HH", packet, dns_offset)
            if outgoing and destination_port == 53:
                stats[index]["physical_dns53_queries"] += 1
                queries[index].add((family, destination, source_port, identifier))
                if b"onetunnel-" in packet[dns_offset + 12:]:
                    stats[index]["physical_dns_canary_queries"] += 1
            if incoming and source_port == 53 and flags & 0x8000:
                replies[index].add((family, source, destination_port, identifier))
    offset += length
summary = []
for index, window in windows.items():
    summary.append({"Index": index, "WindowSeconds": round(window[1] - window[0], 3),
                    **stats[index], "PhysicalDnsQueryReplyPairs": len(queries[index] & replies[index]),
                    "Conclusion": "Observations only; verify capture loss counters and coverage before asserting absence of leaks."})
report = {"Packets": packets, "Checks": summary}
(root / "capture-summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
