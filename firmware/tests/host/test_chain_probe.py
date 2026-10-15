import importlib.util
import pathlib
import unittest

path = pathlib.Path(__file__).resolve().parents[2] / "tools" / "chain_probe.py"
spec = importlib.util.spec_from_file_location("chain_probe", path)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ProbeTests(unittest.TestCase):
    def test_info(self):
        info = probe.parse_info("TRILINK/2 INFO nonce 100000000001 boot=00000064 configured=1 "
            "room=12345678 epoch=00000007 position=1 relay=1 pending=00000000 "
            "result=00000001:1 forwarded=0 received=1 inbox=1")
        self.assertEqual("100000000001", info["mac"])
        self.assertEqual("00000001:1", info["result"])

    def test_malformed_info(self):
        for text in ("", "TRILINK/2 OK x", "TRILINK/2 INFO x NOT_A_MAC",
                     "TRILINK/2 INFO x 100000000001", "TRILINK/2 INFO x 100000000001 boot=1 boot=2"):
            with self.subTest(text=text), self.assertRaises(ValueError):
                probe.parse_info(text)

    def test_boot_and_bytes_checked(self):
        class Fake:
            def __init__(self, destination=False, corrupt=False):
                self.destination, self.corrupt = destination, corrupt
            def info(self):
                return {"mac": "300000000003" if self.destination else "100000000001",
                        "boot": "00000064", "result": "00000001:1"}
            def request(self, command, args=""):
                if command == "SEND":
                    return "TRILINK/2 QUEUED n 00000001"
                if command == "POLL":
                    return "TRILINK/2 DATA n 100000000001 00000064 00000001 " + ("00" if self.corrupt else "01")
                if command == "CONSUME":
                    return "TRILINK/2 OK n CONSUME"
                raise AssertionError(command)
        self.assertEqual(1, probe.send(Fake(), Fake(True), b"\x01")["host_bytes_verified"])
        with self.assertRaises(AssertionError):
            probe.send(Fake(), Fake(True, True), b"\x01")


if __name__ == "__main__":
    unittest.main()
