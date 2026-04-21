#!/usr/bin/env python3
"""Bridge ROS2 IR YOLO topics to the operation-control GUI UDP protocol."""

from __future__ import annotations

import json
import os
import socket
import struct
import threading
from collections import OrderedDict

import cv2
import numpy as np
import rclpy
from rclpy.node import Node
from sensor_msgs.msg import Image
from sentinel_interfaces.msg import Detection2DArray, YoloStatus


DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"
IMAGE_HEADER_SIZE = 20


def getenv_int(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except ValueError:
        return default


def getenv_float(name: str, default: float) -> float:
    try:
        return float(os.getenv(name, str(default)))
    except ValueError:
        return default


GUI_HOST = os.getenv("GUI_HOST", "192.168.1.94")
GUI_PORT = getenv_int("GUI_PORT", 5000)
IMAGE_TOPIC = os.getenv("IMAGE_TOPIC", "/yolo/ir/image_raw")
DETECTION_TOPIC = os.getenv("DETECTION_TOPIC", "/detections/ir")
STATUS_TOPIC = os.getenv("STATUS_TOPIC", "/yolo/ir/status")
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 45)
MAX_UDP_BYTES = getenv_int("MAX_UDP_BYTES", 55000)
STREAM_MAX_WIDTH = getenv_int("STREAM_MAX_WIDTH", 854)
STREAM_MAX_HEIGHT = getenv_int("STREAM_MAX_HEIGHT", 480)
UDP_SEND_BUFFER_BYTES = getenv_int("UDP_SEND_BUFFER_BYTES", 4 * 1024 * 1024)
MIN_DETECTION_SCORE = getenv_float("MIN_DETECTION_SCORE", 0.0)


def stamp_to_ns(stamp) -> int:
    return max(0, int(stamp.sec) * 1_000_000_000 + int(stamp.nanosec))


def resize_for_stream(frame: np.ndarray) -> np.ndarray:
    height, width = frame.shape[:2]
    if width <= 0 or height <= 0:
        return frame

    scale = min(STREAM_MAX_WIDTH / width, STREAM_MAX_HEIGHT / height, 1.0)
    if scale >= 0.999:
        return frame

    new_width = max(2, int(width * scale))
    new_height = max(2, int(height * scale))
    return cv2.resize(frame, (new_width, new_height), interpolation=cv2.INTER_AREA)


def encode_frame_for_udp(frame: np.ndarray) -> bytes | None:
    max_image_payload_bytes = max(1024, MAX_UDP_BYTES - IMAGE_HEADER_SIZE)
    quality_attempts = [
        max(25, JPEG_QUALITY),
        max(25, min(JPEG_QUALITY, 70)),
        55,
        40,
    ]
    scale_attempts = [1.0, 0.85, 0.7, 0.55]

    stream_frame = resize_for_stream(frame)
    for scale in scale_attempts:
        working_frame = stream_frame
        if scale < 0.999:
            new_width = max(2, int(stream_frame.shape[1] * scale))
            new_height = max(2, int(stream_frame.shape[0] * scale))
            working_frame = cv2.resize(stream_frame, (new_width, new_height), interpolation=cv2.INTER_AREA)

        for quality in quality_attempts:
            ok, encoded = cv2.imencode(
                ".jpg",
                working_frame,
                [int(cv2.IMWRITE_JPEG_QUALITY), quality],
            )
            if ok and len(encoded) <= max_image_payload_bytes:
                return encoded.tobytes()

    return None


def build_image_packet(
    encoded_bytes: bytes,
    width: int,
    height: int,
    frame_index: int,
    frame_stamp_ns: int,
) -> bytes:
    header = struct.pack(
        "!QIIHH",
        max(0, frame_stamp_ns),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, min(width, 65535)),
        max(0, min(height, 65535)),
    )
    return header + encoded_bytes


def build_detection_packet(
    frame_stamp_ns: int,
    frame_index: int,
    width: int,
    height: int,
    detections: list[dict],
) -> bytes:
    payload = {
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
        "width": max(0, width),
        "height": max(0, height),
        "detections": detections,
    }
    return DETECTION_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def build_status_packet(
    enabled: bool,
    model_loaded: bool,
    conf_threshold: float,
    last_error: str,
    frame_stamp_ns: int = 0,
    frame_index: int = 0,
) -> bytes:
    payload = {
        "enabled": enabled,
        "modelLoaded": model_loaded,
        "confThreshold": conf_threshold,
        "lastError": last_error,
        "source": "ir",
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


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

    raise ValueError(f"Unsupported IR image encoding: {message.encoding}")


class RosIrToGuiBridge(Node):
    def __init__(self) -> None:
        super().__init__("ros_ir_to_gui_bridge")
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        if UDP_SEND_BUFFER_BYTES > 0:
            self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, UDP_SEND_BUFFER_BYTES)

        self._lock = threading.Lock()
        self._frame_index = 0
        self._stamp_to_gui_frame_id: OrderedDict[int, int] = OrderedDict()
        self._last_width = 0
        self._last_height = 0

        self.create_subscription(Image, IMAGE_TOPIC, self._on_image, 10)
        self.create_subscription(Detection2DArray, DETECTION_TOPIC, self._on_detections, 10)
        self.create_subscription(YoloStatus, STATUS_TOPIC, self._on_status, 10)

        self.get_logger().info(f"IR image topic: {IMAGE_TOPIC}")
        self.get_logger().info(f"IR detection topic: {DETECTION_TOPIC}")
        self.get_logger().info(f"IR status topic: {STATUS_TOPIC}")
        self.get_logger().info(f"Streaming IR GUI packets to {GUI_HOST}:{GUI_PORT}")

    def _remember_frame_id(self, stamp_ns: int, frame_id: int) -> None:
        self._stamp_to_gui_frame_id[stamp_ns] = frame_id
        self._stamp_to_gui_frame_id.move_to_end(stamp_ns)
        while len(self._stamp_to_gui_frame_id) > 240:
            self._stamp_to_gui_frame_id.popitem(last=False)

    def _lookup_frame_id(self, stamp_ns: int, fallback: int) -> int:
        return self._stamp_to_gui_frame_id.get(stamp_ns, fallback)

    def _send(self, packet: bytes) -> None:
        self._sock.sendto(packet, (GUI_HOST, GUI_PORT))

    def _on_image(self, message: Image) -> None:
        try:
            frame = ros_image_to_bgr(message)
            encoded = encode_frame_for_udp(frame)
            if encoded is None:
                self.get_logger().warning("IR frame was too large to encode into one UDP packet.")
                return

            stream_width = 0
            stream_height = 0
            decoded_header = cv2.imdecode(np.frombuffer(encoded, dtype=np.uint8), cv2.IMREAD_COLOR)
            if decoded_header is not None and decoded_header.size > 0:
                stream_height, stream_width = decoded_header.shape[:2]
            else:
                stream_height, stream_width = frame.shape[:2]

            stamp_ns = stamp_to_ns(message.header.stamp)
            with self._lock:
                frame_index = self._frame_index
                self._frame_index += 1
                self._remember_frame_id(stamp_ns, frame_index)
                self._last_width = int(message.width)
                self._last_height = int(message.height)

            packet = build_image_packet(encoded, stream_width, stream_height, frame_index, stamp_ns)
            self._send(packet)
        except Exception as exc:
            self.get_logger().error(f"Failed to forward IR image: {exc}")

    def _on_detections(self, message: Detection2DArray) -> None:
        stamp_ns = stamp_to_ns(message.stamp)
        with self._lock:
            frame_index = self._lookup_frame_id(stamp_ns, int(message.frame_id))
            width = self._last_width
            height = self._last_height

        detections: list[dict] = []
        object_id = 1
        for detection in message.detections:
            score = float(detection.score)
            if score < MIN_DETECTION_SCORE:
                continue

            detections.append(
                {
                    "className": str(detection.class_name),
                    "score": score,
                    "x1": float(detection.x1),
                    "y1": float(detection.y1),
                    "x2": float(detection.x2),
                    "y2": float(detection.y2),
                    "objectId": object_id,
                }
            )
            object_id += 1

        packet = build_detection_packet(stamp_ns, frame_index, width, height, detections)
        try:
            self._send(packet)
        except OSError as exc:
            self.get_logger().warning(f"Failed to forward IR detections: {exc}")

    def _on_status(self, message: YoloStatus) -> None:
        packet = build_status_packet(
            bool(message.enabled),
            bool(message.model_loaded),
            float(message.conf_threshold),
            str(message.last_error),
        )
        try:
            self._send(packet)
        except OSError as exc:
            self.get_logger().warning(f"Failed to forward IR status: {exc}")


def main() -> None:
    rclpy.init()
    node = RosIrToGuiBridge()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
