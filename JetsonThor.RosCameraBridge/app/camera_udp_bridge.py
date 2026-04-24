#!/usr/bin/env python3
"""Bridge ROS2 image and detection topics to the GUI UDP protocol."""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass
import json
import os
import socket
import struct
import threading
import time

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
    stamp = message_or_header.stamp if hasattr(message_or_header, "stamp") else message_or_header
    return int(stamp.sec) * 1_000_000_000 + int(stamp.nanosec)


@dataclass(frozen=True)
class FrameStampInfo:
    stamp_ns: int
    frame_index: int
    width: int
    height: int


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

    def send_image(self, message: Image) -> None:
        frame = ros_image_to_bgr(message)
        source_height, source_width = frame.shape[:2]
        stamp_ns = extract_stamp_ns(message.header)
        self.frame_index += 1
        frame_index = self.frame_index

        stream_frame = fit_frame_to_stream(frame)
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
        stamp_ns = extract_stamp_ns(message.header)
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


class CameraUdpBridge(Node):
    def __init__(self) -> None:
        super().__init__("camera_udp_bridge")
        self._eo = StreamBridge("eo", EO_IMAGE_TOPIC, EO_DETECTION_TOPIC, GUI_HOST, EO_GUI_PORT)
        self._ir = StreamBridge("ir", IR_IMAGE_TOPIC, IR_DETECTION_TOPIC, GUI_HOST, IR_GUI_PORT)

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


def main() -> None:
    rclpy.init()
    node = CameraUdpBridge()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
