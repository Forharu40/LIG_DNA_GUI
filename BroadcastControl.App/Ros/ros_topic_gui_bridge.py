#!/usr/bin/env python3
"""Subscribe to ROS2 image/detection topics and feed the WPF GUI UDP receiver."""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
import json
import os
import socket
import struct
import time

import cv2
import numpy as np
import rclpy
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image
from sentinel_interfaces.msg import Detection2DArray


DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"
GUI_HOST = os.getenv("GUI_HOST", "127.0.0.1")
EO_GUI_PORT = int(os.getenv("EO_GUI_PORT", os.getenv("GUI_PORT", "6000")))
IR_GUI_PORT = int(os.getenv("IR_GUI_PORT", "6001"))
EO_IMAGE_TOPIC = os.getenv("EO_IMAGE_TOPIC", "/video/eo/preprocessed")
IR_IMAGE_TOPIC = os.getenv("IR_IMAGE_TOPIC", "/camera/ir")
EO_DETECTION_TOPIC = os.getenv("EO_DETECTION_TOPIC", "/detections/eo")
IR_DETECTION_TOPIC = os.getenv("IR_DETECTION_TOPIC", "/detections/ir")
JPEG_QUALITY = int(os.getenv("ROS_BRIDGE_JPEG_QUALITY", "70"))
MAX_STAMP_CACHE = int(os.getenv("ROS_BRIDGE_MAX_STAMP_CACHE", "120"))
STAMP_TOLERANCE_NS = int(float(os.getenv("ROS_BRIDGE_STAMP_TOLERANCE_MS", "80")) * 1_000_000)


@dataclass(frozen=True)
class StreamConfig:
    name: str
    image_topic: str
    detection_topic: str
    gui_port: int


def stamp_to_ns(stamp) -> int:
    return int(stamp.sec) * 1_000_000_000 + int(stamp.nanosec)


def image_to_bgr(message: Image) -> np.ndarray:
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


def build_status_packet(source: str, frame_index: int, stamp_ns: int = 0, error: str = "") -> bytes:
    payload = {
        "enabled": True,
        "modelLoaded": True,
        "confThreshold": 0.0,
        "lastError": error,
        "source": source,
        "stampNs": max(0, stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


class StreamState:
    def __init__(self, config: StreamConfig, sock: socket.socket) -> None:
        self.config = config
        self.sock = sock
        self.frame_index = 0
        self.latest_width = 0
        self.latest_height = 0
        self.stamp_to_frame: OrderedDict[int, int] = OrderedDict()
        self.pending_detections: OrderedDict[int, list[dict]] = OrderedDict()

    def remember_frame(self, stamp_ns: int, frame_index: int) -> None:
        self.stamp_to_frame[stamp_ns] = frame_index
        self.stamp_to_frame.move_to_end(stamp_ns)
        while len(self.stamp_to_frame) > MAX_STAMP_CACHE:
            self.stamp_to_frame.popitem(last=False)

    def resolve_frame_index(self, stamp_ns: int) -> int:
        if stamp_ns in self.stamp_to_frame:
            return self.stamp_to_frame[stamp_ns]

        best_stamp = None
        best_gap = STAMP_TOLERANCE_NS + 1
        for candidate_stamp in self.stamp_to_frame.keys():
            gap = abs(candidate_stamp - stamp_ns)
            if gap < best_gap:
                best_gap = gap
                best_stamp = candidate_stamp

        if best_stamp is not None and best_gap <= STAMP_TOLERANCE_NS:
            return self.stamp_to_frame[best_stamp]

        return self.frame_index

    def send_image(self, message: Image) -> None:
        frame = image_to_bgr(message)
        ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY])
        if not ok:
            raise RuntimeError("JPEG encoding failed")

        stamp_ns = stamp_to_ns(message.header.stamp) or time.time_ns()
        self.frame_index += 1
        self.latest_width = frame.shape[1]
        self.latest_height = frame.shape[0]
        self.remember_frame(stamp_ns, self.frame_index)

        packet = build_image_packet(
            encoded.tobytes(),
            self.latest_width,
            self.latest_height,
            self.frame_index,
            stamp_ns,
        )
        self.sock.sendto(packet, (GUI_HOST, self.config.gui_port))

        pending = self.pending_detections.pop(stamp_ns, None)
        if pending is not None:
            self.send_detection(stamp_ns, pending)

    def send_detection(self, stamp_ns: int, detections: list[dict]) -> None:
        frame_index = self.resolve_frame_index(stamp_ns)
        packet = build_detection_packet(
            stamp_ns,
            frame_index,
            self.latest_width,
            self.latest_height,
            detections,
        )
        self.sock.sendto(packet, (GUI_HOST, self.config.gui_port))

    def handle_detection_message(self, message: Detection2DArray) -> None:
        stamp_ns = stamp_to_ns(message.stamp) or time.time_ns()
        detections = [
            {
                "className": detection.class_name,
                "score": float(detection.score),
                "x1": float(detection.x1),
                "y1": float(detection.y1),
                "x2": float(detection.x2),
                "y2": float(detection.y2),
                "objectId": index,
            }
            for index, detection in enumerate(message.detections, start=1)
        ]

        if self.stamp_to_frame:
            self.send_detection(stamp_ns, detections)
            return

        self.pending_detections[stamp_ns] = detections
        while len(self.pending_detections) > MAX_STAMP_CACHE:
            self.pending_detections.popitem(last=False)


class GuiRosTopicBridge(Node):
    def __init__(self) -> None:
        super().__init__("gui_ros_topic_bridge")
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.streams = [
            StreamState(StreamConfig("EO", EO_IMAGE_TOPIC, EO_DETECTION_TOPIC, EO_GUI_PORT), self.sock),
            StreamState(StreamConfig("IR", IR_IMAGE_TOPIC, IR_DETECTION_TOPIC, IR_GUI_PORT), self.sock),
        ]

        for state in self.streams:
            self.create_subscription(
                Image,
                state.config.image_topic,
                lambda message, stream_state=state: self.on_image(stream_state, message),
                qos_profile_sensor_data,
            )
            self.create_subscription(
                Detection2DArray,
                state.config.detection_topic,
                lambda message, stream_state=state: self.on_detection(stream_state, message),
                qos_profile_sensor_data,
            )
            self.get_logger().info(
                f"{state.config.name}: {state.config.image_topic} + {state.config.detection_topic} "
                f"-> {GUI_HOST}:{state.config.gui_port}"
            )
            self.sock.sendto(
                build_status_packet(state.config.image_topic, 0),
                (GUI_HOST, state.config.gui_port),
            )

    def on_image(self, state: StreamState, message: Image) -> None:
        try:
            state.send_image(message)
        except Exception as exc:
            self.get_logger().error(f"{state.config.name} image bridge failed: {exc}")
            self.sock.sendto(
                build_status_packet(state.config.image_topic, state.frame_index, error=str(exc)),
                (GUI_HOST, state.config.gui_port),
            )

    def on_detection(self, state: StreamState, message: Detection2DArray) -> None:
        try:
            state.handle_detection_message(message)
        except Exception as exc:
            self.get_logger().error(f"{state.config.name} detection bridge failed: {exc}")

    def destroy_node(self) -> bool:
        self.sock.close()
        return super().destroy_node()


def main() -> None:
    rclpy.init()
    node = GuiRosTopicBridge()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
