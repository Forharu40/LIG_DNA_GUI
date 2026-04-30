#!/usr/bin/env python3
"""Serve recorded videos from the Jetson video folder for the GUI."""

from __future__ import annotations

import html
import json
import os
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import quote


VIDEO_FILE_EXTENSIONS = {".mp4", ".avi", ".mov", ".mkv", ".m4v"}
RECORDING_DIR = os.getenv("RECORDING_DIR", "/home/lig/Desktop/video")
RECORDING_HTTP_PORT = int(os.getenv("RECORDING_HTTP_PORT", "8090"))


class RecordingVideoHandler(SimpleHTTPRequestHandler):
    def log_message(self, format: str, *args) -> None:
        return

    def do_GET(self) -> None:
        if self.path == "/api/videos":
            self._send_video_list()
            return

        if self.path in {"/", "/index.html"}:
            self._send_player_page()
            return

        super().do_GET()

    def _video_files(self) -> list[Path]:
        directory = Path(self.directory)
        return sorted(
            [
                item
                for item in directory.iterdir()
                if item.is_file() and item.suffix.lower() in VIDEO_FILE_EXTENSIONS
            ],
            key=lambda item: item.stat().st_mtime,
            reverse=True,
        )

    def _send_video_list(self) -> None:
        payload = [
            {
                "name": item.name,
                "url": quote(item.name),
                "sizeBytes": item.stat().st_size,
                "modifiedUnixMs": int(item.stat().st_mtime * 1000),
            }
            for item in self._video_files()
        ]
        encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(encoded)

    def _send_player_page(self) -> None:
        files = [item.name for item in self._video_files()]
        options = "\n".join(
            f'<option value="{quote(name)}">{html.escape(name)}</option>' for name in files
        )
        initial_source = quote(files[0]) if files else ""
        body = f"""<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Recorded Videos</title>
</head>
<body>
  <h1>Recorded Videos</h1>
  <select id="fileList">{options}</select>
  {('<video id="player" controls src="' + initial_source + '" style="width:100%;max-height:80vh;background:#000"></video>') if files else '<p>No recorded videos.</p>'}
  <script>
    const fileList = document.getElementById('fileList');
    const player = document.getElementById('player');
    if (fileList && player) {{
      fileList.addEventListener('change', () => {{
        player.src = fileList.value;
        player.load();
      }});
    }}
  </script>
</body>
</html>"""
        encoded = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


def main() -> None:
    directory = Path(RECORDING_DIR)
    directory.mkdir(parents=True, exist_ok=True)
    handler = partial(RecordingVideoHandler, directory=str(directory))
    server = ThreadingHTTPServer(("0.0.0.0", RECORDING_HTTP_PORT), handler)
    print(f"Serving {directory} at http://0.0.0.0:{RECORDING_HTTP_PORT}/", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
