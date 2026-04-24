#!/usr/bin/env python3
"""Forward /camera/eo and /camera/ir ROS2 images to GUI UDP JPEG streams."""

from __future__ import annotations

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


def getenv_int(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except ValueError:
        return default


GUI_HOST = os.getenv("GUI_HOST", "192.168.1.94")
EO_GUI_PORT = getenv_int("EO_GUI_PORT", 5000)
IR_GUI_PORT = getenv_int("IR_GUI_PORT", 5001)
EO_IMAGE_TOPIC = os.getenv("EO_IMAGE_TOPIC", "/camera/eo")
IR_IMAGE_TOPIC = os.getenv("IR_IMAGE_TOPIC", "/camera/ir")
STREAM_WIDTH = getenv_int("STREAM_WIDTH", 640)
STREAM_HEIGHT = getenv_int("STREAM_HEIGHT", 360)
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 35)
UDP_SEND_BUFFER_BYTES = getenv_int("UDP_SEND_BUFFER_BYTES", 4 * 1024 * 1024)


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


def build_packet(encoded_bytes: bytes, frame_index: int) -> bytes:
    header = struct.pack(
        "!QIIHH",
        time.time_ns(),
        max(0, frame_index),
        len(encoded_bytes),
        STREAM_WIDTH,
        STREAM_HEIGHT,
    )
    return header + encoded_bytes


class UdpImageForwarder:
    def __init__(self, name: str, host: str, port: int) -> None:
        self.name = name
        self.host = host
        self.port = port
        self.frame_index = 0
        self.first_frame_logged = False
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        if UDP_SEND_BUFFER_BYTES > 0:
            self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, UDP_SEND_BUFFER_BYTES)

    def send(self, message: Image) -> None:
        frame = ros_image_to_bgr(message)
        frame = cv2.resize(frame, (STREAM_WIDTH, STREAM_HEIGHT), interpolation=cv2.INTER_AREA)
        ok, encoded = cv2.imencode(
            ".jpg",
            frame,
            [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
        )
        if not ok:
            raise RuntimeError(f"{self.name.upper()} JPEG encode failed")

        packet = build_packet(encoded.tobytes(), self.frame_index)
        self.sock.sendto(packet, (self.host, self.port))
        self.frame_index += 1


class CameraUdpBridge(Node):
    def __init__(self) -> None:
        super().__init__("camera_udp_bridge")
        self._eo = UdpImageForwarder("eo", GUI_HOST, EO_GUI_PORT)
        self._ir = UdpImageForwarder("ir", GUI_HOST, IR_GUI_PORT)

        self.create_subscription(Image, EO_IMAGE_TOPIC, self._on_eo_image, qos_profile_sensor_data)
        self.create_subscription(Image, IR_IMAGE_TOPIC, self._on_ir_image, qos_profile_sensor_data)

        self.get_logger().info(f"EO image topic: {EO_IMAGE_TOPIC}")
        self.get_logger().info(f"IR image topic: {IR_IMAGE_TOPIC}")
        self.get_logger().info(f"Streaming EO UDP packets to {GUI_HOST}:{EO_GUI_PORT}")
        self.get_logger().info(f"Streaming IR UDP packets to {GUI_HOST}:{IR_GUI_PORT}")

    def _on_eo_image(self, message: Image) -> None:
        try:
            self._eo.send(message)
            if not self._eo.first_frame_logged:
                self.get_logger().info("EO first frame sent!")
                self._eo.first_frame_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward EO image: {exc}")

    def _on_ir_image(self, message: Image) -> None:
        try:
            self._ir.send(message)
            if not self._ir.first_frame_logged:
                self.get_logger().info("IR first frame sent!")
                self._ir.first_frame_logged = True
        except Exception as exc:
            self.get_logger().error(f"Failed to forward IR image: {exc}")


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
