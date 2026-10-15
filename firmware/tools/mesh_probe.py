"""Explicit-port v0.8 USB/radio smoke test, 2..6 boards, unordered roster.

CONFIG provisions a test roster, NOT authenticated Room admission. No flashing,
port scanning, PC network, automatic relay cut, or implicit inbox consumption.
Close other owners of the selected native USB ports before running.
"""
from __future__ import annotations

import argparse
import json
import re
import time
from contextlib import ExitStack

# Reuse the bounded nonce-matched USB reader and byte/identity verification.
# Its v0.7 main() remains a historical fixed-chain diagnostic only.
from chain_probe import Session as BaseSession, parse_info as legacy_info, send


def parse_info(line: str) -> dict:
    if not line.startswith("TRILINK/3 INFO "):
        raise ValueError("not a v3 INFO response")
    info = legacy_info(line.replace("TRILINK/3", "TRILINK/2", 1))
    if info.get("route_format") != "2":
        raise ValueError("route format 2 required")
    for key in ("boot", "room", "epoch", "pending"):
        if not re.fullmatch(r"[0-9A-F]{8}", info[key]):
            raise ValueError(f"invalid {key}")
    if int(info["boot"], 16) == 0:
        raise ValueError("zero boot identity")
    if info["mac"] == "000000000000" or int(info["mac"][:2], 16) & 1:
        raise ValueError("invalid unicast identity")
    for key, maximum in (("members", 6), ("inbox", 8), ("position", 6),
                         ("configured", 1), ("relay", 1)):
        value = info.get(key, "")
        if not re.fullmatch(r"[0-9]+", value) or not 0 <= int(value) <= maximum:
            raise ValueError(f"invalid {key}")
    return info


def parse_table(line: str) -> dict:
    fields = line.split()
    if len(fields) != 8 or fields[:2] != ["TRILINK/3", "TABLE"]:
        raise ValueError("invalid TABLE response")
    values = {}
    for field in fields[3:]:
        key, separator, value = field.partition("=")
        if not separator or key in values:
            raise ValueError("malformed/duplicate TABLE field")
        values[key] = value
    if set(values) != {"count", "local", "neighbors", "members", "routes"}:
        raise ValueError("unknown/missing TABLE field")
    if not re.fullmatch(r"[1-6]", values["count"]) or not re.fullmatch(r"[1-6]", values["local"]):
        raise ValueError("TABLE must be configured")
    count, local = int(values["count"]), int(values["local"])
    members = values["members"].split(",")
    if local > count or len(members) != count or sorted(set(members)) != members:
        raise ValueError("invalid canonical roster")
    if any(not re.fullmatch(r"[0-9A-F]{12}", m) or m == "000000000000"
           or int(m[:2], 16) & 1 for m in members):
        raise ValueError("invalid member MAC")
    if not re.fullmatch(r"[0-9A-F]{2}", values["neighbors"]):
        raise ValueError("invalid neighbor mask")
    neighbors = int(values["neighbors"], 16)
    if neighbors >> count or neighbors & (1 << (local - 1)):
        raise ValueError("neighbor mask outside roster or includes self")
    routes = []
    for route in values["routes"].split(","):
        if not re.fullmatch(r"[0-6]/[0-5]", route):
            raise ValueError("invalid route")
        via, hops = map(int, route.split("/"))
        if via > count or hops >= count or bool(via) != bool(hops):
            raise ValueError("route outside roster")
        if via and (via == local or not neighbors & (1 << (via - 1))):
            raise ValueError("next hop is not a direct neighbor")
        routes.append((via, hops))
    if len(routes) != count or routes[local - 1] != (0, 0):
        raise ValueError("invalid self route or route count")
    return {"count": count, "local": local, "members": members,
            "neighbors": neighbors, "routes": routes}


class Session(BaseSession):
    WIRE_VERSION = "TRILINK/3"

    def __init__(self, port: str):
        super().__init__(port)
        self._last_request = 0.0

    def request(self, command: str, arguments: str = "", timeout: float = 2.0) -> str:
        # Explicit diagnostics, <= 10 requests/s/port; never a background poller.
        delay = 0.1 - (time.monotonic() - self._last_request)
        if delay > 0:
            time.sleep(delay)
        self._last_request = time.monotonic()
        return super().request(command, arguments, timeout)

    def info(self) -> dict:
        return parse_info(self.request("INFO"))


def preflight(identities: list[dict], room: str, epoch: str) -> list[str]:
    if not 2 <= len(identities) <= 6:
        raise ValueError("2..6 boards required")
    roster = sorted(i["mac"] for i in identities)
    if len(set(roster)) != len(roster):
        raise ValueError("ports identify duplicate boards")
    for info in identities:
        if info["inbox"] != "0" or info["pending"] != "00000000":
            raise RuntimeError("traffic exists; refusing CONFIG before changing anything")
        if info["configured"] == "1" and (info["room"] != room
                or int(info["epoch"], 16) > int(epoch, 16)):
            raise RuntimeError("different Room or older epoch; explicit reboot/reprovision required")
    return roster


def wait_routes(sessions: list, roster: list[str], timeout: float = 12.0) -> list[dict]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        tables = [parse_table(s.request("TABLE")) for s in sessions]
        if all(t["members"] == roster
               and all(i == t["local"] - 1 or hops > 0
                       for i, (_, hops) in enumerate(t["routes"])) for t in tables):
            return tables
        time.sleep(0.25)
    raise TimeoutError("no full connectivity before deadline; inspect TABLE and physical links")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ports", nargs="+", required=True,
                        help="2..6 distinct native USB COMs; order is not a route")
    parser.add_argument("--room", default="54524931")
    parser.add_argument("--epoch", required=True, help="shared nonzero 8-digit hex test epoch")
    args = parser.parse_args()
    if not 2 <= len(args.ports) <= 6 or len(set(p.upper() for p in args.ports)) != len(args.ports):
        parser.error("2..6 distinct explicit ports required")
    for value in (args.room, args.epoch):
        if not re.fullmatch(r"[0-9a-fA-F]{8}", value) or int(value, 16) == 0:
            parser.error("room/epoch must be nonzero 8-digit hex")
    room, epoch = args.room.upper(), args.epoch.upper()
    with ExitStack() as stack:
        sessions = [stack.enter_context(Session(port)) for port in args.ports]
        identities = [s.info() for s in sessions]
        roster = preflight(identities, room, epoch)
        # Same-epoch roster conflicts are detected on every board before any write.
        for session, info in zip(sessions, identities):
            if info["configured"] == "1" and info["epoch"] == epoch:
                if parse_table(session.request("TABLE"))["members"] != roster:
                    raise RuntimeError("same-epoch roster conflict; choose newer epoch")
        for session in sessions:
            reply = session.request("CONFIG", f"{room} {epoch} {' '.join(roster)}")
            if reply.split()[1:2] != ["OK"]:
                raise RuntimeError(reply)
        for session, before in zip(sessions, identities):
            after = session.info()
            if (after["mac"] != before["mac"] or after["boot"] != before["boot"]
                    or after["configured"] != "1" or after["room"] != room
                    or after["epoch"] != epoch or int(after["members"]) != len(roster)):
                raise RuntimeError("identity/configuration changed during provisioning")
        tables = wait_routes(sessions, roster)
        evidence = []
        for i, source in enumerate(sessions):
            for j, target in enumerate(sessions):
                if i != j:
                    evidence.append(send(source, target, f"mesh:{i}->{j}".encode() + b"\x00\xff"))
        evidence.append(send(sessions[0], sessions[-1], bytes(range(132))))
        print(json.dumps({"software_version": "0.8.0", "result": "MESH_USB_SMOKE_PASS",
                          "ports": args.ports, "roster": roster, "tables": tables,
                          "transfers": evidence, "final": [s.info() for s in sessions],
                          "rf_out_of_range_verified": False,
                          "distributed_room_verified": False,
                          "device_execution_verified": False}, indent=2))


if __name__ == "__main__":
    main()
