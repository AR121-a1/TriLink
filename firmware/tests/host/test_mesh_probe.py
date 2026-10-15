import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "tools"))
import mesh_probe as probe

INFO = ("TRILINK/3 INFO nonce 100000000001 boot=00000064 configured=1 "
        "room=12345678 epoch=00000007 position=1 relay=1 pending=00000000 "
        "result=00000001:1 forwarded=0 received=1 inbox=0 members=3 route_format=2")
TABLE = ("TRILINK/3 TABLE n count=3 local=1 neighbors=02 "
         "members=100000000001,200000000002,300000000003 routes=0/0,2/1,2/2")


class ProbeTests(unittest.TestCase):
    def test_info(self):
        self.assertEqual("3", probe.parse_info(INFO)["members"])
        for bad in (INFO.replace("/3", "/2"), INFO.replace("format=2", "format=1"),
                    INFO.replace("members=3", "members=7"), INFO.replace("00000064", "0"),
                    INFO.replace("inbox=0", "inbox=9"), INFO.replace("100000000001", "110000000001")):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                probe.parse_info(bad)

    def test_table(self):
        table = probe.parse_table(TABLE)
        self.assertEqual([(0, 0), (2, 1), (2, 2)], table["routes"])
        for bad in (TABLE.replace("count=3", "count=7"), TABLE.replace("local=1", "local=4"),
                    TABLE.replace("neighbors=02", "neighbors=03"),
                    TABLE.replace("neighbors=02", "neighbors=80"),
                    TABLE.replace("2/2", "3/1"), TABLE.replace("0/0", "2/1"),
                    TABLE.replace("2/2", "2/0"), TABLE.replace("2/2", "2/3"),
                    TABLE.replace("300000000003", "200000000002"),
                    TABLE + " count=3", TABLE.replace("routes=", "unknown=")):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                probe.parse_table(bad)

    def test_preflight_no_destructive_reconfigure(self):
        a = probe.parse_info(INFO)
        b = dict(a, mac="200000000002")
        self.assertEqual([a["mac"], b["mac"]], probe.preflight([b, a], "12345678", "00000007"))
        for altered in (dict(b, inbox="1"), dict(b, pending="00000001"),
                        dict(b, room="87654321"), dict(b, epoch="00000008"), a):
            with self.subTest(altered=altered), self.assertRaises((RuntimeError, ValueError)):
                probe.preflight([a, altered], "12345678", "00000007")

    def test_convergence_bounded(self):
        class Fake:
            def request(self, command):
                return TABLE
        self.assertEqual(1, len(probe.wait_routes([Fake()], probe.parse_table(TABLE)["members"])))
        with self.assertRaises(TimeoutError):
            probe.wait_routes([Fake()], [], timeout=0)

    def test_v3_wire_and_nonce(self):
        class Port:
            port = "TEST_ONLY"
            def write(self, wire):
                parts = wire.decode().split()
                self.wire = wire
                self.reply = iter((f"TRILINK/2 INFO {parts[2]} wrong\n"
                                   f"TRILINK/3 OK {parts[2]} RELAY\n").encode())
                return len(wire)
            def read(self, count):
                return bytes([next(self.reply)])
        session = probe.Session.__new__(probe.Session)
        session.port, session._last_request = Port(), 0
        self.assertTrue(session.request("RELAY", "1").startswith("TRILINK/3 OK "))
        self.assertTrue(session.port.wire.startswith(b"TRILINK/3 RELAY "))


if __name__ == "__main__":
    unittest.main()
