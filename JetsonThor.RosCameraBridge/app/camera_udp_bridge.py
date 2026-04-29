#!/usr/bin/env python3
"""Bridge ROS2 image and detection topics to the GUI UDP protocol."""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass
from datetime import datetime
from functools import partial
import html
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import socket
import struct
import threading
import time
from urllib.parse import quote

import cv2
import numpy as np
import rclpy
from rclpy.node import Node
from rclpy.qos import (
    DurabilityPolicy,
    HistoryPolicy,
    QoSProfile,
    ReliabilityPolicy,
    qos_profile_sensor_data,
)
from sensor_msgs.msg import Image
from sentinel_interfaces.msg import Detection2DArray


LEGACY_IMAGE_HEADER_SIZE = 20
DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"
STAMP_HISTORY_LIMIT = 120


def getenv_int(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except ValueError:
        return default


def getenv_bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


GUI_HOST = os.getenv("GUI_HOST", "192.168.1.94")
EO_GUI_PORT = getenv_int("EO_GUI_PORT", 5000)
IR_GUI_PORT = getenv_int("IR_GUI_PORT", 5001)
EO_IMAGE_TOPIC = os.getenv("EO_IMAGE_TOPIC", "/video/eo/preprocessed")
IR_IMAGE_TOPIC = os.getenv("IR_IMAGE_TOPIC", "/video/ir/preprocessed")
EO_DETECTION_TOPIC = os.getenv("EO_DETECTION_TOPIC", "/detections/eo")
IR_DETECTION_TOPIC = os.getenv("IR_DETECTION_TOPIC", "/detections/ir")
STREAM_WIDTH = getenv_int("STREAM_WIDTH", 640)
STREAM_HEIGHT = getenv_int("STREAM_HEIGHT", 360)
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 35)
UDP_SEND_BUFFER_BYTES = getenv_int("UDP_SEND_BUFFER_BYTES", 4 * 1024 * 1024)
SEND_STATUS_WITH_IMAGE = getenv_bool("SEND_STATUS_WITH_IMAGE", True)
RECORDING_ENABLED = getenv_bool("RECORDING_ENABLED", True)
RECORDING_DIR = os.getenv("RECORDING_DIR", "/recordings")
RECORDING_SEGMENT_SECONDS = getenv_int("RECORDING_SEGMENT_SECONDS", 300)
RECORDING_FPS = getenv_int("RECORDING_FPS", 15)
RECORDING_HTTP_ENABLED = getenv_bool("RECORDING_HTTP_ENABLED", True)
RECORDING_HTTP_PORT = getenv_int("RECORDING_HTTP_PORT", 8090)

IMAGE_TOPIC_QOS = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=5,
    reliability=ReliabilityPolicy.RELIABLE,
    durability=DurabilityPolicy.VOLATILE,
)

DETECTION_TOPIC_QOS = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=20,
    reliability=ReliabilityPolicy.RELIABLE,
    durability=DurabilityPolicy.VOLATILE,
)


def ros_image_to_bgr(message: Image) -> np.ndarray:
    encoding = message.encoding.lower()
    height = int(message.height)
    width = int(message.width)
    step = int(message.step)
    data = np.frombuffer(message.data, dtype=np.uint8)

    if encoding in {"bgr8", "rgb8"}:
        channels = 3
        row = data.reshape(height, step)[:, : width * channels]
        image = row.reshape(height, width, channels)
        if encoding == "rgb8":
            image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
        return image

    if encoding in {"bgra8", "rgba8"}:
        channels = 4
        row = data.reshape(height, step)[:, : width * channels]
        image = row.reshape(height, width, channels)
        if encoding == "rgba8":
            return cv2.cvtColor(image, cv2.COLOR_RGBA2BGR)
        return cv2.cvtColor(image, cv2.COLOR_BGRA2BGR)

    if encoding in {"mono8", "8uc1"}:
        row = data.reshape(height, step)[:, :width]
        return cv2.cvtColor(row.reshape(height, width), cv2.COLOR_GRAY2BGR)

    raise ValueError(f"Unsupported image encoding: {message.encoding}")


def build_image_packet(encoded_bytes: bytes, width: int, height: int, frame_index: int, stamp_ns: int) -> bytes:
    header = struct.pack(
        "!QIIHH",
        max(0, stamp_ns),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, width),
        max(0, height),
    )
    return header + encoded_bytes


def build_detection_packet(stamp_ns: int, frame_index: int, width: int, height: int, detections: list[dict]) -> bytes:
    payload = {
        "stampNs": max(0, stamp_ns),
        "frameId": max(0, frame_index),
        "width": max(0, width),
        "height": max(0, height),
        "detections": detections,
    }
    return DETECTION_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def build_status_packet(source: str, stamp_ns: int, frame_index: int, last_error: str = "") -> bytes:
    payload = {
        "enabled": True,
        "modelLoaded": True,
        "confThreshold": 0.0,
        "lastError": last_error,
        "source": source,
        "stampNs": max(0, stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def fit_frame_to_stream(frame: np.ndarray) -> np.ndarray:
    height, width = frame.shape[:2]
    scale = min(STREAM_WIDTH / width, STREAM_HEIGHT / height, 1.0)
    target_width = max(2, int(width * scale))
    target_height = max(2, int(height * scale))
    if target_width == width and target_height == height:
        return frame
    return cv2.resize(frame, (target_width, target_height), interpolation=cv2.INTER_AREA)


def extract_stamp_ns(message_or_header) -> int:
    if hasattr(message_or_header, "header") and hasattr(message_or_header.header, "stamp"):
        stamp = message_or_header.header.stamp
    elif hasattr(message_or_header, "stamp"):
        stamp = message_or_header.stamp
    else:
        stamp = message_or_header
    return int(stamp.sec) * 1_000_000_000 + int(stamp.nanosec)


@dataclass(frozen=True)
class FrameStampInfo:
    stamp_ns: int
    frame_index: int
    width: int
    height: int


class VideoSegmentRecorder:
    def __init__(self, name: str, directory: str, segment_seconds: int, fps: int) -> None:
        self.name = name
        self.directory = Path(directory)
        self.segment_seconds = max(10, segment_seconds)
        self.fps = max(1, fps)
        self._lock = threading.Lock()
        self._writer: cv2.VideoWriter | None = None
        self._segment_started_at = 0.0
        self._frame_size: tuple[int, int] | None = None
        self._current_path: Path | None = None
        self.directory.mkdir(parents=True, exist_ok=True)

    def write(self, frame: np.ndarray) -> None:
        if frame.size == 0:
            return

        height, width = frame.shape[:2]
        now = time.monotonic()

        with self._lock:
            needs_new_segment = (
                self._writer is None
                or self._frame_size != (width, height)
                or now - self._segment_started_at >= self.segment_seconds
            )
            if needs_new_segment:
                self._start_segment(width, height, now)

            if self._writer is not None:
                self._writer.write(frame)

    def close(self) -> None:
        with self._lock:
            if self._writer is not None:
                self._writer.release()
                self._writer = None

    def _start_segment(self, width: int, height: int, now: float) -> None:
        if self._writer is not None:
            self._writer.release()

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        self._current_path = self.directory / f"{self.name}_{timestamp}.mp4"
        fourcc = cv2.VideoWriter_fourcc(*"mp4v")
        writer = cv2.VideoWriter(str(self._current_path), fourcc, self.fps, (width, height))
        if not writer.isOpened():
            self._writer = None
            raise RuntimeError(f"Failed to open recording file: {self._current_path}")

        self._writer = writer
        self._segment_started_at = now
        self._frame_size = (width, height)


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

    def _send_video_list(self) -> None:
        directory = Path(self.directory)
        files = sorted(
            [item for item in directory.glob("*.mp4") if item.is_file()],
            key=lambda item: item.stat().st_mtime,
            reverse=True,
        )
        payload = [
            {
                "name": item.name,
                "url": quote(item.name),
                "sizeBytes": item.stat().st_size,
                "modifiedUnixMs": int(item.stat().st_mtime * 1000),
            }
            for item in files
        ]
        encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(encoded)

    def _send_player_page(self) -> None:
        directory = Path(self.directory)
        files = sorted(
            [item.name for item in directory.glob("*.mp4") if item.is_file()],
            reverse=True,
        )
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
  <style>
    body {{ margin: 0; padding: 24px; background: #111827; color: #e5e7eb; font-family: sans-serif; }}
    h1 {{ margin: 0 0 16px; font-size: 24px; }}
    .bar {{ display: flex; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }}
    select {{ min-height: 38px; padding: 6px 10px; background: #1f2937; color: #f9fafb; border: 1px solid #4b5563; }}
    video {{ width: 100%; max-height: 72vh; background: #000; }}
    .empty {{ padding: 32px; border: 1px solid #374151; background: #1f2937; }}
  </style>
</head>
<body>
  <h1>녹화 영상 보기</h1>
  <div class="bar">
    <select id="fileList">{options}</select>
    <select id="speed">
      <option value="0.25">0.25x</option>
      <option value="0.5">0.5x</option>
      <option value="0.75">0.75x</option>
      <option value="1" selected>1.0x</option>
      <option value="1.5">1.5x</option>
      <option value="2">2.0x</option>
    </select>
  </div>
  {('<video id="player" controls src="' + initial_source + '"></video>') if files else '<div class="empty">저장된 영상이 아직 없습니다.</div>'}
  <script>
    const fileList = document.getElementById('fileList');
    const speed = document.getElementById('speed');
    const player = document.getElementById('player');
    if (fileList && player) {{
      fileList.addEventListener('change', () => {{
        player.src = fileList.value;
        player.load();
      }});
      speed.addEventListener('change', () => {{
        player.playbackRate = Number(speed.value);
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


class StreamBridge:
    def __init__(self, name: str, image_topic: str, detection_topic: str, host: str, port: int) -> None:
        self.name = name
        self.image_topic = image_topic
        self.detection_topic = detection_topic
        self.host = host
        self.port = port
        self.frame_index = 0
        self.first_image_logged = False
        self.first_detection_logged = False
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        if UDP_SEND_BUFFER_BYTES > 0:
            self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, UDP_SEND_BUFFER_BYTES)

        self._lock = threading.Lock()
        self._stamp_history: deque[FrameStampInfo] = deque(maxlen=STAMP_HISTORY_LIMIT)
        self._last_frame_info = FrameStampInfo(0, 0, 0, 0)
        self._recorder = (
            VideoSegmentRecorder(name, RECORDING_DIR, RECORDING_SEGMENT_SECONDS, RECORDING_FPS)
            if RECORDING_ENABLED
            else None
        )

    def send_image(self, message: Image) -> None:
        frame = ros_image_to_bgr(message)
        source_height, source_width = frame.shape[:2]
        stamp_ns = extract_stamp_ns(message.header)
        self.frame_index += 1
        frame_index = self.frame_index

        stream_frame = fit_frame_to_stream(frame)
        if self._recorder is not None:
            self._recorder.write(stream_frame)

        ok, encoded = cv2.imencode(
            ".jpg",
            stream_frame,
            [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
        )
        if not ok:
            raise RuntimeError(f"{self.name.upper()} JPEG encode failed")

        packet = build_image_packet(
            encoded.tobytes(),
            stream_frame.shape[1],
            stream_frame.shape[0],
            frame_index,
            stamp_ns,
        )
        self.sock.sendto(packet, (self.host, self.port))

        frame_info = FrameStampInfo(stamp_ns, frame_index, source_width, source_height)
        with self._lock:
            self._stamp_history.append(frame_info)
            self._last_frame_info = frame_info

        if SEND_STATUS_WITH_IMAGE:
            status_packet = build_status_packet(self.image_topic, stamp_ns, frame_index)
            self.sock.sendto(status_packet, (self.host, self.port))

    def send_detection(self, message: Detection2DArray) -> None:
        stamp_ns = extract_stamp_ns(message)
        with self._lock:
            frame_info = self._match_frame_info(stamp_ns)

        detections: list[dict] = []
        for index, detection in enumerate(message.detections, start=1):
            x1 = float(detection.x1)
            y1 = float(detection.y1)
            x2 = float(detection.x2)
            y2 = float(detection.y2)
            if x2 <= x1 or y2 <= y1:
                continue

            detections.append(
                {
                    "className": detection.class_name,
                    "score": float(detection.score),
                    "x1": x1,
                    "y1": y1,
                    "x2": x2,
                    "y2": y2,
                    "objectId": index,
                }
            )

        packet = build_detection_packet(
            stamp_ns if stamp_ns > 0 else frame_info.stamp_ns,
            frame_info.frame_index,
            frame_info.width,
            frame_info.height,
            detections,
        )
        self.sock.sendto(packet, (self.host, self.port))

    def _match_frame_info(self, stamp_ns: int) -> FrameStampInfo:
        if stamp_ns > 0:
            for item in reversed(self._stamp_history):
                if item.stamp_ns == stamp_ns:
                    return item

        if self._last_frame_info.frame_index > 0:
            return self._last_frame_info

        return FrameStampInfo(stamp_ns, 0, 0, 0)

    def close(self) -> None:
        if self._recorder is not None:
            self._recorder.close()


class CameraUdpBridge(Node):
    def __init__(self) -> None:
        super().__init__("camera_udp_bridge")
        self._eo = StreamBridge("eo", EO_IMAGE_TOPIC, EO_DETECTION_TOPIC, GUI_HOST, EO_GUI_PORT)
        self._ir = StreamBridge("ir", IR_IMAGE_TOPIC, IR_DETECTION_TOPIC, GUI_HOST, IR_GUI_PORT)
        self._recording_http_server: ThreadingHTTPServer | None = None
        self._recording_http_thread: threading.Thread | None = None

        self.create_subscription(Image, EO_IMAGE_TOPIC, self._on_eo_image, IMAGE_TOPIC_QOS)
        self.create_subscription(Image, IR_IMAGE_TOPIC, self._on_ir_image, IMAGE_TOPIC_QOS)
        self.create_subscription(Detection2DArray, EO_DETECTION_TOPIC, self._on_eo_detection, DETECTION_TOPIC_QOS)
        self.create_subscription(Detection2DArray, IR_DETECTION_TOPIC, self._on_ir_detection, DETECTION_TOPIC_QOS)

        self.get_logger().info(f"EO image topic: {EO_IMAGE_TOPIC}")
        self.get_logger().info(f"IR image topic: {IR_IMAGE_TOPIC}")
        self.get_logger().info(f"EO detection topic: {EO_DETECTION_TOPIC}")
        self.get_logger().info(f"IR detection topic: {IR_DETECTION_TOPIC}")
        self.get_logger().info(f"Streaming EO UDP packets to {GUI_HOST}:{EO_GUI_PORT}")
        self.get_logger().info(f"Streaming IR UDP packets to {GUI_HOST}:{IR_GUI_PORT}")
        if RECORDING_ENABLED:
            self.get_logger().info(
                f"Recording EO/IR videos to {RECORDING_DIR} every {RECORDING_SEGMENT_SECONDS} seconds"
            )
        self._start_recording_http_server()

    def _on_eo_image(self, message: Image) -> None:
        try:
            self._eo.send_image(message)
            if not self._eo.first_image_logged:
                self.get_logger().info("EO first image sent!")
                self._eo.first_image_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward EO image: {exc}")

    def _on_ir_image(self, message: Image) -> None:
        try:
            self._ir.send_image(message)
            if not self._ir.first_image_logged:
                self.get_logger().info("IR first image sent!")
                self._ir.first_image_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward IR image: {exc}")

    def _on_eo_detection(self, message: Detection2DArray) -> None:
        try:
            self._eo.send_detection(message)
            if not self._eo.first_detection_logged:
                self.get_logger().info("EO first detection packet sent!")
                self._eo.first_detection_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward EO detections: {exc}")

    def _on_ir_detection(self, message: Detection2DArray) -> None:
        try:
            self._ir.send_detection(message)
            if not self._ir.first_detection_logged:
                self.get_logger().info("IR first detection packet sent!")
                self._ir.first_detection_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward IR detections: {exc}")

    def close(self) -> None:
        self._eo.close()
        self._ir.close()
        if self._recording_http_server is not None:
            self._recording_http_server.shutdown()
            self._recording_http_server.server_close()

    def _start_recording_http_server(self) -> None:
        if not RECORDING_ENABLED or not RECORDING_HTTP_ENABLED:
            return

        Path(RECORDING_DIR).mkdir(parents=True, exist_ok=True)
        handler = partial(RecordingVideoHandler, directory=RECORDING_DIR)
        self._recording_http_server = ThreadingHTTPServer(("0.0.0.0", RECORDING_HTTP_PORT), handler)
        self._recording_http_thread = threading.Thread(
            target=self._recording_http_server.serve_forever,
            name="recording-video-http",
            daemon=True,
        )
        self._recording_http_thread.start()
        self.get_logger().info(f"Recorded video player: http://0.0.0.0:{RECORDING_HTTP_PORT}/")


def main() -> None:
    rclpy.init()
    node = CameraUdpBridge()
    try:
        rclpy.spin(node)
    finally:
        node.close()
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
