#!/usr/bin/env python3
"""Run YOLO on an EO ROS topic and forward GUI-compatible UDP packets."""

from __future__ import annotations

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
from ultralytics import YOLO


LEGACY_IMAGE_HEADER_SIZE = 20
DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"


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


def getenv_bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


GUI_HOST = os.getenv("GUI_HOST", "192.168.1.94")
GUI_PORT = getenv_int("GUI_PORT", 5000)
INPUT_IMAGE_TOPIC = os.getenv("INPUT_IMAGE_TOPIC", "/video/eo/preprocessed")
OUTPUT_IMAGE_TOPIC = os.getenv("OUTPUT_IMAGE_TOPIC", "/yolo/eo/image_raw")
PUBLISH_OUTPUT_TOPIC = getenv_bool("PUBLISH_OUTPUT_TOPIC", True)
MODEL_PATH = os.getenv("MODEL_PATH", "/opt/models/yolo11s.pt").strip()
CONFIDENCE = getenv_float("CONFIDENCE", 0.60)
INFERENCE_SIZE = getenv_int("INFERENCE_SIZE", 640)
STREAM_MAX_WIDTH = max(320, getenv_int("STREAM_MAX_WIDTH", 854))
STREAM_MAX_HEIGHT = max(240, getenv_int("STREAM_MAX_HEIGHT", 480))
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 45)
MAX_UDP_BYTES = getenv_int("MAX_UDP_BYTES", 55000)


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


def bgr_to_ros_image(frame: np.ndarray, source_message: Image) -> Image:
    message = Image()
    message.header = source_message.header
    message.height = int(frame.shape[0])
    message.width = int(frame.shape[1])
    message.encoding = "bgr8"
    message.is_bigendian = 0
    message.step = int(frame.shape[1] * frame.shape[2])
    message.data = frame.tobytes()
    return message


def fit_frame_to_stream(frame: np.ndarray) -> np.ndarray:
    height, width = frame.shape[:2]
    scale = min(STREAM_MAX_WIDTH / width, STREAM_MAX_HEIGHT / height, 1.0)
    target_width = max(2, int(width * scale))
    target_height = max(2, int(height * scale))
    if target_width == width and target_height == height:
        return frame
    return cv2.resize(frame, (target_width, target_height), interpolation=cv2.INTER_AREA)


def build_image_packet(encoded_bytes: bytes, width: int, height: int, frame_index: int, frame_stamp_ns: int) -> bytes:
    header = struct.pack(
        "!QIIHH",
        max(0, frame_stamp_ns),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, width),
        max(0, height),
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
    source: str,
    frame_stamp_ns: int,
    frame_index: int,
) -> bytes:
    payload = {
        "enabled": enabled,
        "modelLoaded": model_loaded,
        "confThreshold": conf_threshold,
        "lastError": last_error,
        "source": source,
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def encode_frame_for_udp(frame: np.ndarray) -> bytes | None:
    max_image_payload_bytes = max(1024, MAX_UDP_BYTES - LEGACY_IMAGE_HEADER_SIZE)
    quality_attempts = [
        max(25, JPEG_QUALITY),
        max(25, min(JPEG_QUALITY, 70)),
        55,
        40,
    ]

    for quality in quality_attempts:
        ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), quality])
        if ok and len(encoded) <= max_image_payload_bytes:
            return encoded.tobytes()

    return None


def extract_detections(results, source_width: int, source_height: int) -> list[dict]:
    if not results:
        return []

    result = results[0]
    boxes = result.boxes
    names = result.names
    if boxes is None or len(boxes) == 0:
        return []

    xyxy = boxes.xyxy.cpu().numpy().astype(float)
    cls_ids = boxes.cls.cpu().numpy().astype(int)
    scores = boxes.conf.cpu().numpy().astype(float)

    detections: list[dict] = []
    for index, (box, cls_id, score) in enumerate(zip(xyxy, cls_ids, scores), start=1):
        x1, y1, x2, y2 = box.tolist()
        mapped_x1 = max(0.0, min(source_width, x1))
        mapped_y1 = max(0.0, min(source_height, y1))
        mapped_x2 = max(0.0, min(source_width, x2))
        mapped_y2 = max(0.0, min(source_height, y2))
        if mapped_x2 - mapped_x1 < 2 or mapped_y2 - mapped_y1 < 2:
            continue

        detections.append(
            {
                "className": names.get(cls_id, str(cls_id)),
                "score": float(score),
                "x1": mapped_x1,
                "y1": mapped_y1,
                "x2": mapped_x2,
                "y2": mapped_y2,
                "objectId": index,
            }
        )

    return detections


class WebcamTopicYoloBridge(Node):
    def __init__(self) -> None:
        super().__init__("webcam_topic_yolo_bridge")
        self._frame_index = 0
        self._model_loaded = False
        self._last_error = ""
        self._first_frame_logged = False
        self._first_gui_frame_logged = False
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._model = YOLO(MODEL_PATH)
        self._model_loaded = True
        self._output_publisher = None

        if PUBLISH_OUTPUT_TOPIC:
            self._output_publisher = self.create_publisher(Image, OUTPUT_IMAGE_TOPIC, qos_profile_sensor_data)

        self.create_subscription(Image, INPUT_IMAGE_TOPIC, self._on_image, qos_profile_sensor_data)

        self.get_logger().info(f"Input image topic: {INPUT_IMAGE_TOPIC}")
        if PUBLISH_OUTPUT_TOPIC:
            self.get_logger().info(f"Output image topic: {OUTPUT_IMAGE_TOPIC}")
        self.get_logger().info(f"Streaming GUI packets to {GUI_HOST}:{GUI_PORT}")
        self.get_logger().info(f"Using model: {MODEL_PATH}")

    def _on_image(self, message: Image) -> None:
        frame_stamp_ns = (
            int(message.header.stamp.sec) * 1_000_000_000
            + int(message.header.stamp.nanosec)
        )

        try:
            frame = ros_image_to_bgr(message)
            self._frame_index += 1

            if not self._first_frame_logged:
                self.get_logger().info("First webcam topic frame received.")
                self._first_frame_logged = True

            results = self._model.predict(
                frame,
                conf=CONFIDENCE,
                imgsz=INFERENCE_SIZE,
                verbose=False,
            )
            detections = extract_detections(results, frame.shape[1], frame.shape[0])

            stream_frame = fit_frame_to_stream(frame)
            encoded_bytes = encode_frame_for_udp(stream_frame)
            if encoded_bytes is None:
                raise RuntimeError("JPEG encode failed within the UDP payload limit")

            image_packet = build_image_packet(
                encoded_bytes,
                stream_frame.shape[1],
                stream_frame.shape[0],
                self._frame_index,
                frame_stamp_ns or time.time_ns(),
            )
            detection_packet = build_detection_packet(
                frame_stamp_ns or time.time_ns(),
                self._frame_index,
                frame.shape[1],
                frame.shape[0],
                detections,
            )
            status_packet = build_status_packet(
                enabled=True,
                model_loaded=self._model_loaded,
                conf_threshold=CONFIDENCE,
                last_error="",
                source=INPUT_IMAGE_TOPIC,
                frame_stamp_ns=frame_stamp_ns or time.time_ns(),
                frame_index=self._frame_index,
            )

            self._sock.sendto(image_packet, (GUI_HOST, GUI_PORT))
            self._sock.sendto(detection_packet, (GUI_HOST, GUI_PORT))
            self._sock.sendto(status_packet, (GUI_HOST, GUI_PORT))

            if not self._first_gui_frame_logged:
                self.get_logger().info("First EO frame sent to GUI.")
                self._first_gui_frame_logged = True

            if self._output_publisher is not None:
                self._output_publisher.publish(bgr_to_ros_image(stream_frame, message))
        except Exception as exc:
            self._last_error = str(exc)
            status_packet = build_status_packet(
                enabled=True,
                model_loaded=self._model_loaded,
                conf_threshold=CONFIDENCE,
                last_error=self._last_error,
                source=INPUT_IMAGE_TOPIC,
                frame_stamp_ns=frame_stamp_ns or time.time_ns(),
                frame_index=self._frame_index,
            )
            self._sock.sendto(status_packet, (GUI_HOST, GUI_PORT))
            self.get_logger().error(f"Failed to process webcam EO topic frame: {exc}")


def main() -> None:
    rclpy.init()
    node = WebcamTopicYoloBridge()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
