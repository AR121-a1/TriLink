"""Real USB/ESP-NOW chain smoke test. Never flashes or uses a PC network.

Requires three native USB data ports with v0.7 firmware. Provisioning is an
explicit fixed-chain test fixture, NOT Room joining/approval. A passing smoke
test does not establish RF range, distributed Room, or device execution.
"""
from __future__ import annotations

import argparse
import json
import re
import secrets
import time
from contextlib import ExitStack


def parse_info(line: str) -> dict:
    fields = line.split()
    if len(fields) < 4 or fields[:2] != ["TRILINK/2", "INFO"]:
        raise ValueError("not a v2 INFO response")
    if not re.fullmatch(r"[0-9A-F]{12}", fields[3]):
        raise ValueError("invalid MAC")
    info = {"mac": fields[3]}
    for item in fields[4:]:
        key, sep, value = item.partition("=")
        if not sep or key in info:
            raise ValueError("malformed/duplicate INFO field")
        info[key] = value
    for key in ("boot", "configured", "room", "epoch", "position", "relay", "pending",
                "result", "forwarded", "received", "inbox"):
        if key not in info:
            raise ValueError(f"INFO missing {key}")
    if not re.fullmatch(r"[0-9A-F]{8}:[012]", info["result"]):
        raise ValueError("invalid result")
    return info


class Session:
    """One owner, bounded reads and nonce matching; never guesses a port."""

    WIRE_VERSION = "TRILINK/2"

    def __init__(self, port: str):
        import serial
        self.port = serial.Serial()
        self.port.port = port
        self.port.baudrate = 115200
        self.port.timeout = 0.15
        self.port.write_timeout = 0.4
        self.port.dtr = False
        self.port.rts = False
        self.port.open()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.port.close()

    def request(self, command: str, arguments: str = "", timeout: float = 2.0) -> str:
        nonce = secrets.token_hex(4)
        line = f"{self.WIRE_VERSION} {command} {nonce}" + (f" {arguments}" if arguments else "") + "\n"
        if len(line) > 384:
            raise ValueError("command exceeds firmware line budget")
        wire = line.encode("ascii")
        if self.port.write(wire) != len(wire):
            raise IOError("short USB write")
        deadline = time.monotonic() + timeout
        buffer = bytearray()
        discard = False
        while time.monotonic() < deadline:
            byte = self.port.read(1)
            if not byte:
                continue
            if byte == b"\n":
                response = bytes(buffer).decode("ascii", errors="replace").strip()
                buffer.clear()
                if discard:
                    discard = False
                    continue
                fields = response.split()
                if len(fields) >= 3 and fields[0] == self.WIRE_VERSION and fields[2] == nonce:
                    if fields[1] == "ERROR":
                        raise RuntimeError(response)
                    return response
            elif not discard:
                if len(buffer) >= 511:
                    buffer.clear()
                    discard = True
                else:
                    buffer.extend(byte)
        raise TimeoutError(f"{self.port.port}: no matching {command} reply")

    def info(self) -> dict:
        return parse_info(self.request("INFO"))


def wait_result(source: Session, message: str, timeout: float = 8.0) -> int:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        info = source.info()
        result_id, result = info["result"].split(":")
        if result_id == message and result != "0":
            return int(result)
        time.sleep(0.25)
    raise TimeoutError("firmware did not produce a bounded end-to-end result")


def send(source: Session, destination: Session, body: bytes, expected: int = 1) -> dict:
    before = source.info()
    target = destination.info()
    start = time.monotonic()
    response = source.request("SEND", f"{target['mac']} {body.hex()}").split()
    if len(response) != 4 or response[1] != "QUEUED" or not re.fullmatch(r"[0-9A-F]{8}", response[3]):
        raise ValueError("missing QUEUED operation id")
    message = response[3]
    result = wait_result(source, message)
    if result != expected:
        raise AssertionError(f"expected result {expected}, got {result}")
    evidence = {"source": before["mac"], "destination": target["mac"], "message": message,
                "result": result, "elapsed_ms": round((time.monotonic()-start)*1000, 1)}
    if result == 1:
        # ACK alone proves destination inbox, not host delivery. Verify bytes
        # separately, including the sender boot session and message identity.
        received = destination.request("POLL").split()
        if (len(received) != 7 or received[1] != "DATA" or received[3] != before["mac"]
                or received[4] != before["boot"] or received[5] != message
                or bytes.fromhex(received[6]) != body):
            raise AssertionError(f"destination data mismatch: {received}")
        evidence["host_bytes_verified"] = len(body)
        consumed = destination.request("CONSUME", " ".join(received[3:6]))
        if " OK " not in consumed:
            raise AssertionError("destination did not confirm inbox consumption")
    return evidence


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ports", nargs=3, required=True, metavar=("NODE1", "NODE2", "NODE3"),
                        help="explicit native USB data COMs in chain order; close GUI/monitors first")
    parser.add_argument("--room", default="54524931")
    parser.add_argument("--epoch", required=True, help="shared nonzero 8-digit hex fixture epoch; reboot to change")
    args = parser.parse_args()
    if len(set(p.upper() for p in args.ports)) != 3:
        parser.error("three distinct ports are required")
    for value in (args.room, args.epoch):
        if not re.fullmatch(r"[0-9a-fA-F]{8}", value) or int(value, 16) == 0:
            parser.error("room and epoch must be nonzero 8-digit hex")
    with ExitStack() as stack:
        sessions = [stack.enter_context(Session(port)) for port in args.ports]
        identities = [s.info() for s in sessions]
        chain = [i["mac"] for i in identities]
        if len(set(chain)) != 3:
            raise AssertionError("ports do not identify three different boards")
        for session in sessions:
            reply = session.request("CONFIG", f"{args.room} {args.epoch} {' '.join(chain)}")
            if " OK " not in reply:
                raise AssertionError(reply)
        for i, session in enumerate(sessions):
            info = session.info()
            if info["position"] != str(i+1) or info["configured"] != "1":
                raise AssertionError("configuration did not take effect")
            if info["inbox"] != "0" or info["pending"] != "00000000":
                raise RuntimeError("existing traffic present; do not consume someone else's inbox")
        previous_relay = sessions[1].info()["relay"]
        evidence = []
        try:
            sessions[1].request("RELAY", "1")
            evidence.append(send(sessions[0], sessions[2], b"node1->node3\x00\xff"))
            evidence.append(send(sessions[2], sessions[0], bytes(range(136))))
            baseline = int(sessions[1].info()["forwarded"])
            sessions[1].request("RELAY", "0")
            evidence.append(send(sessions[0], sessions[2], b"must-time-out", expected=2))
            if int(sessions[1].info()["forwarded"]) != baseline:
                raise AssertionError("relay forwarded while disabled")
            if sessions[2].info()["inbox"] != "0":
                raise AssertionError("negative-control payload was unexpectedly delivered")
            sessions[1].request("RELAY", "1")
            evidence.append(send(sessions[0], sessions[2], b"recovered"))
            if int(sessions[1].info()["forwarded"]) < baseline + 2:
                raise AssertionError("missing forward and return relay evidence")
            print(json.dumps({"software_version": "0.7.0", "ports": args.ports, "chain": chain,
                              "result": "CHAIN_USB_SMOKE_PASS", "transfers": evidence,
                              "final": [s.info() for s in sessions],
                              "rf_out_of_range_verified": False,
                              "distributed_room_verified": False,
                              "device_execution_verified": False}, indent=2))
        finally:
            sessions[1].request("RELAY", previous_relay)


if __name__ == "__main__":
    main()
