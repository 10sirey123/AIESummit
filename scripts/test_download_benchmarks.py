"""Exercise interrupted-download recovery against a real local HTTP server."""

import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

import download_benchmarks as benchmarks


class DownloadRecoveryTests(unittest.TestCase):
    def run_download(self, interrupt_first=False, existing_prefix=False):
        payload = b"benchmark fixture: original bytes"
        requests = []

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.0"

            def log_message(self, *_):
                pass

            def do_GET(self):
                requested_range = self.headers.get("Range")
                requests.append(requested_range)
                if interrupt_first and len(requests) == 1:
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload[:8])
                    self.wfile.flush()
                    self.close_connection = True
                elif interrupt_first and requested_range:
                    offset = int(requested_range.removeprefix("bytes=").removesuffix("-"))
                    remaining = payload[offset:]
                    self.send_response(206)
                    self.send_header("Content-Range", f"bytes {offset}-{len(payload)-1}/{len(payload)}")
                    self.send_header("Content-Length", str(len(remaining)))
                    self.end_headers()
                    self.wfile.write(remaining)
                else:
                    # A server may ignore Range and return the entire file.
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload)

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                entry = {"path": "fixture.bin", "size": len(payload),
                         "checksum": "sha256:" + hashlib.sha256(payload).hexdigest(),
                         "url": f"http://127.0.0.1:{server.server_port}/fixture"}
                if existing_prefix:
                    (root / "fixture.bin.part").write_bytes(payload[:8])
                with patch.object(benchmarks, "ROOT", root), patch.object(benchmarks.time, "sleep"):
                    result = benchmarks.fetch(entry, False)
                self.assertEqual((root / "fixture.bin").read_bytes(), payload)
                self.assertFalse((root / "fixture.bin.part").exists())
                self.assertEqual(result["sha256"], hashlib.sha256(payload).hexdigest())
        finally:
            server.shutdown()
            server.server_close()
            thread.join()
        return requests

    def test_premature_eof_resumes_from_saved_prefix(self):
        self.assertEqual(self.run_download(interrupt_first=True), [None, "bytes=8-"])

    def test_range_ignored_replaces_existing_partial(self):
        self.assertEqual(self.run_download(existing_prefix=True), ["bytes=8-"])


if __name__ == "__main__":
    unittest.main()
