#!/usr/bin/env python3
"""ROS2 camera YOLO node that also forwards GUI-compatible UDP packets."""

from __future__ import annotations

import json
import os
import socket
import struct
import threading
import time
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass

import cv2
import numpy as np
import rclpy
from builtin_interfaces.msg import Time as RosTime
from rclpy.node import Node
from sensor_msgs.msg import Image
from sentinel_interfaces.msg import Detection, Detection2D, Detection2DArray, YoloStatus
from ultralytics import YOLO


IMAGE_PACKET_HEADER_SIZE = 20
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
SYNCED_IMAGE_TOPIC = os.getenv("SYNCED_IMAGE_TOPIC", "/yolo/eo/image_raw")
DETECTION_TOPIC = os.getenv("DETECTION_TOPIC", "/detections/eo")
DRIVER_DETECTION_TOPIC = os.getenv("DRIVER_DETECTION_TOPIC", "/driver/eo/detection")
STATUS_TOPIC = os.getenv("STATUS_TOPIC", "/yolo/eo/status")
OUTPUT_FRAME_ID = os.getenv("OUTPUT_FRAME_ID", "gui_camera_eo")

MODEL_PATH = os.getenv("MODEL_PATH", "/ros2_ws/src/yolo_detector_pkg/model/best.onnx").strip()
MODEL_INPUT_SIZE = getenv_int("MODEL_INPUT_SIZE", 640)
CONFIDENCE = getenv_float("CONFIDENCE", 0.60)
DETECTION_INTERVAL_SECONDS = max(0.05, getenv_float("DETECTION_INTERVAL_SECONDS", 0.20))

STREAM_MAX_WIDTH = max(320, getenv_int("STREAM_MAX_WIDTH", 854))
STREAM_MAX_HEIGHT = max(240, getenv_int("STREAM_MAX_HEIGHT", 480))
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 45)
MAX_UDP_BYTES = getenv_int("MAX_UDP_BYTES", 55000)
UDP_SEND_BUFFER_BYTES = max(0, getenv_int("UDP_SEND_BUFFER_BYTES", 4 * 1024 * 1024))
STATUS_INTERVAL_SECONDS = max(0.10, getenv_float("STATUS_INTERVAL_SECONDS", 1.0))
ENABLE_GUI_FORWARD = getenv_bool("ENABLE_GUI_FORWARD", True)
ENABLE_TILE_INFERENCE = getenv_bool("ENABLE_TILE_INFERENCE", False)
TILE_OVERLAP_RATIO = max(0.0, min(0.45, getenv_float("TILE_OVERLAP_RATIO", 0.15)))

ALLOWED_CLASSES = {
    name.strip().lower()
    for name in os.getenv("ALLOWED_CLASSES", "").split(",")
    if name.strip()
}


@dataclass(frozen=True)
class EncodedImageResult:
    stamp_ns: int
    frame_index: int
    width: int
    height: int
    encoded_bytes: bytes


@dataclass(frozen=True)
class DetectionResult:
    stamp_sec: int
    stamp_nanosec: int
    stamp_ns: int
    frame_index: int
    width: int
    height: int
    detections: list[dict]


def stamp_to_ns(stamp: RosTime) -> int:
    return max(0, int(stamp.sec) * 1_000_000_000 + int(stamp.nanosec))


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


def bgr_to_ros_image(frame: np.ndarray, stamp: RosTime, topic_frame_id: str) -> Image:
    message = Image()
    message.header.stamp = stamp
    message.header.frame_id = topic_frame_id
    message.height = int(frame.shape[0])
    message.width = int(frame.shape[1])
    message.encoding = "bgr8"
    message.is_bigendian = False
    message.step = int(frame.shape[1] * 3)
    message.data = frame.tobytes()
    return message


def resize_for_stream(frame: np.ndarray) -> np.ndarray:
    height, width = frame.shape[:2]
    if width <= 0 or height <= 0:
        return frame

    scale = min(STREAM_MAX_WIDTH / width, STREAM_MAX_HEIGHT / height, 1.0)
    if scale >= 0.999:
        return frame

    resized_width = max(2, int(width * scale))
    resized_height = max(2, int(height * scale))
    return cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_AREA)


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
    source: str,
    frame_stamp_ns: int = 0,
    frame_index: int = 0,
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


def encode_stream_frame(frame: np.ndarray, stamp_ns: int, frame_index: int) -> EncodedImageResult | None:
    max_image_payload_bytes = max(1024, MAX_UDP_BYTES - IMAGE_PACKET_HEADER_SIZE)
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
                return EncodedImageResult(
                    stamp_ns=stamp_ns,
                    frame_index=frame_index,
                    width=int(working_frame.shape[1]),
                    height=int(working_frame.shape[0]),
                    encoded_bytes=encoded.tobytes(),
                )

    return None


def calculate_iou(a: dict, b: dict) -> float:
    inter_x1 = max(a["x1"], b["x1"])
    inter_y1 = max(a["y1"], b["y1"])
    inter_x2 = min(a["x2"], b["x2"])
    inter_y2 = min(a["y2"], b["y2"])

    inter_width = max(0.0, inter_x2 - inter_x1)
    inter_height = max(0.0, inter_y2 - inter_y1)
    inter_area = inter_width * inter_height
    if inter_area <= 0:
        return 0.0

    area_a = max(0.0, a["x2"] - a["x1"]) * max(0.0, a["y2"] - a["y1"])
    area_b = max(0.0, b["x2"] - b["x1"]) * max(0.0, b["y2"] - b["y1"])
    denominator = area_a + area_b - inter_area
    if denominator <= 0:
        return 0.0

    return inter_area / denominator


def append_result_detections(
    results,
    output: list[dict],
    offset_x: int,
    offset_y: int,
    source_width: int,
    source_height: int,
) -> None:
    if not results:
        return

    result = results[0]
    boxes = result.boxes
    names = result.names

    if boxes is None or len(boxes) == 0:
        return

    xyxy = boxes.xyxy.cpu().numpy().astype(float)
    cls_ids = boxes.cls.cpu().numpy().astype(int)
    scores = boxes.conf.cpu().numpy().astype(float)

    for box, cls_id, score in zip(xyxy, cls_ids, scores):
        x1, y1, x2, y2 = box.tolist()
        mapped_x1 = max(0.0, min(source_width, x1 + offset_x))
        mapped_y1 = max(0.0, min(source_height, y1 + offset_y))
        mapped_x2 = max(0.0, min(source_width, x2 + offset_x))
        mapped_y2 = max(0.0, min(source_height, y2 + offset_y))
        if mapped_x2 - mapped_x1 < 2 or mapped_y2 - mapped_y1 < 2:
            continue

        output.append(
            {
                "className": names.get(cls_id, str(cls_id)),
                "score": float(score),
                "x1": mapped_x1,
                "y1": mapped_y1,
                "x2": mapped_x2,
                "y2": mapped_y2,
            }
        )


def suppress_duplicate_detections(detections: list[dict]) -> list[dict]:
    if not detections:
        return []

    sorted_detections = sorted(detections, key=lambda item: item["score"], reverse=True)
    filtered: list[dict] = []
    for candidate in sorted_detections:
        is_duplicate = False
        for kept in filtered:
            if kept["className"] != candidate["className"]:
                continue

            if calculate_iou(kept, candidate) >= 0.45:
                is_duplicate = True
                break

        if not is_duplicate:
            filtered.append(candidate)

    for index, detection in enumerate(filtered, start=1):
        detection["objectId"] = index

    return filtered


def filter_allowed_detections(detections: list[dict]) -> list[dict]:
    if not ALLOWED_CLASSES:
        return detections

    return [
        detection
        for detection in detections
        if str(detection["className"]).lower() in ALLOWED_CLASSES
    ]


def detect_objects(model: YOLO, frame: np.ndarray) -> list[dict]:
    source_height, source_width = frame.shape[:2]
    detections: list[dict] = []

    full_frame_results = model.predict(
        frame,
        conf=CONFIDENCE,
        imgsz=MODEL_INPUT_SIZE,
        verbose=False,
    )
    append_result_detections(full_frame_results, detections, 0, 0, source_width, source_height)

    if ENABLE_TILE_INFERENCE:
        overlap_x = int(source_width * TILE_OVERLAP_RATIO)
        overlap_y = int(source_height * TILE_OVERLAP_RATIO)
        tile_width = max(64, (source_width // 2) + overlap_x)
        tile_height = max(64, (source_height // 2) + overlap_y)
        step_x = max(32, tile_width - overlap_x)
        step_y = max(32, tile_height - overlap_y)

        for top in range(0, source_height, step_y):
            for left in range(0, source_width, step_x):
                bottom = min(source_height, top + tile_height)
                right = min(source_width, left + tile_width)
                tile = frame[top:bottom, left:right]
                if tile.size == 0:
                    continue

                tile_results = model.predict(
                    tile,
                    conf=CONFIDENCE,
                    imgsz=MODEL_INPUT_SIZE,
                    verbose=False,
                )
                append_result_detections(tile_results, detections, left, top, source_width, source_height)

                if right >= source_width:
                    break

            if bottom >= source_height:
                break

    return suppress_duplicate_detections(filter_allowed_detections(detections))


def detect_objects_for_frame(
    model: YOLO,
    frame: np.ndarray,
    stamp_sec: int,
    stamp_nanosec: int,
    frame_index: int,
) -> DetectionResult:
    detections = detect_objects(model, frame)
    return DetectionResult(
        stamp_sec=stamp_sec,
        stamp_nanosec=stamp_nanosec,
        stamp_ns=(stamp_sec * 1_000_000_000) + stamp_nanosec,
        frame_index=frame_index,
        width=int(frame.shape[1]),
        height=int(frame.shape[0]),
        detections=detections,
    )


class GuiCameraYoloNode(Node):
    def __init__(self) -> None:
        super().__init__("gui_camera_yolo_node")

        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        if UDP_SEND_BUFFER_BYTES > 0:
            self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, UDP_SEND_BUFFER_BYTES)

        self._lock = threading.Lock()
        self._frame_index = 0
        self._last_detection_monotonic: float | None = None
        self._last_status_sent_monotonic = 0.0
        self._last_status_signature: tuple | None = None
        self._last_error = ""
        self._model_loaded = False

        self._detection_executor = ThreadPoolExecutor(max_workers=1)
        self._encode_executor = ThreadPoolExecutor(max_workers=1)
        self._send_executor = ThreadPoolExecutor(max_workers=1)
        self._pending_detection: Future[DetectionResult] | None = None
        self._pending_encode: Future[EncodedImageResult | None] | None = None

        self._image_publisher = self.create_publisher(Image, SYNCED_IMAGE_TOPIC, 10)
        self._detection_publisher = self.create_publisher(Detection2DArray, DETECTION_TOPIC, 10)
        self._driver_detection_publisher = self.create_publisher(Detection, DRIVER_DETECTION_TOPIC, 10)
        self._status_publisher = self.create_publisher(YoloStatus, STATUS_TOPIC, 10)
        self.create_subscription(Image, INPUT_IMAGE_TOPIC, self._on_image, 10)
        self.create_timer(0.02, self._on_timer)

        try:
            self._model = YOLO(MODEL_PATH)
            self._model_loaded = True
        except Exception as exc:
            self._model = None
            self._last_error = f"YOLO model load failed: {exc}"
            self.get_logger().error(self._last_error)

        self.get_logger().info(f"Input image topic: {INPUT_IMAGE_TOPIC}")
        self.get_logger().info(f"Synced image topic: {SYNCED_IMAGE_TOPIC}")
        self.get_logger().info(f"Detection topic: {DETECTION_TOPIC}")
        self.get_logger().info(f"Driver detection topic: {DRIVER_DETECTION_TOPIC}")
        self.get_logger().info(f"Status topic: {STATUS_TOPIC}")
        self.get_logger().info(f"GUI UDP target: {GUI_HOST}:{GUI_PORT}")
        self._publish_status()

    def destroy_node(self) -> bool:
        self._detection_executor.shutdown(wait=False, cancel_futures=True)
        self._encode_executor.shutdown(wait=False, cancel_futures=True)
        self._send_executor.shutdown(wait=False, cancel_futures=True)
        self._sock.close()
        return super().destroy_node()

    def _next_frame_index(self) -> int:
        with self._lock:
            self._frame_index += 1
            return self._frame_index

    def _send_packet_async(self, packet: bytes) -> None:
        if not ENABLE_GUI_FORWARD:
            return

        def send_once() -> None:
            self._sock.sendto(packet, (GUI_HOST, GUI_PORT))

        self._send_executor.submit(send_once)

    def _on_image(self, message: Image) -> None:
        try:
            frame = ros_image_to_bgr(message)
        except Exception as exc:
            self._last_error = f"Image conversion failed: {exc}"
            self.get_logger().warning(self._last_error)
            return

        republished = bgr_to_ros_image(frame, message.header.stamp, OUTPUT_FRAME_ID)
        self._image_publisher.publish(republished)

        stamp_ns = stamp_to_ns(message.header.stamp)
        if stamp_ns <= 0:
            stamp_ns = time.time_ns()

        frame_index = self._next_frame_index()

        if self._pending_encode is None:
            self._pending_encode = self._encode_executor.submit(
                encode_stream_frame,
                frame.copy(),
                stamp_ns,
                frame_index,
            )

        should_run_detection = (
            self._model_loaded and
            self._model is not None and
            self._pending_detection is None and
            (
                self._last_detection_monotonic is None or
                (time.monotonic() - self._last_detection_monotonic) >= DETECTION_INTERVAL_SECONDS
            )
        )

        if should_run_detection:
            self._pending_detection = self._detection_executor.submit(
                detect_objects_for_frame,
                self._model,
                frame.copy(),
                int(message.header.stamp.sec),
                int(message.header.stamp.nanosec),
                frame_index,
            )
            self._last_detection_monotonic = time.monotonic()

    def _on_timer(self) -> None:
        if self._pending_encode is not None and self._pending_encode.done():
            try:
                encoded = self._pending_encode.result()
                if encoded is not None:
                    packet = build_image_packet(
                        encoded.encoded_bytes,
                        encoded.width,
                        encoded.height,
                        encoded.frame_index,
                        encoded.stamp_ns,
                    )
                    self._send_packet_async(packet)
            except Exception as exc:
                self._last_error = f"JPEG encode failed: {exc}"
                self.get_logger().warning(self._last_error)
            finally:
                self._pending_encode = None

        if self._pending_detection is not None and self._pending_detection.done():
            try:
                result = self._pending_detection.result()
                self._publish_detection_result(result)
                packet = build_detection_packet(
                    result.stamp_ns,
                    result.frame_index,
                    result.width,
                    result.height,
                    result.detections,
                )
                self._send_packet_async(packet)
            except Exception as exc:
                self._last_error = f"YOLO detection failed: {exc}"
                self.get_logger().warning(self._last_error)
            finally:
                self._pending_detection = None

        if (time.monotonic() - self._last_status_sent_monotonic) >= STATUS_INTERVAL_SECONDS:
            self._publish_status()

    def _publish_detection_result(self, result: DetectionResult) -> None:
        array_message = Detection2DArray()
        array_message.stamp.sec = int(result.stamp_sec)
        array_message.stamp.nanosec = int(result.stamp_nanosec)
        array_message.frame_id = int(result.frame_index)

        for detection in result.detections:
            item = Detection2D()
            item.class_name = str(detection["className"])
            item.score = float(detection["score"])
            item.x1 = float(detection["x1"])
            item.y1 = float(detection["y1"])
            item.x2 = float(detection["x2"])
            item.y2 = float(detection["y2"])
            array_message.detections.append(item)

        self._detection_publisher.publish(array_message)

        driver_message = Detection()
        if result.detections:
            primary = max(result.detections, key=lambda item: item["score"])
            driver_message.cx = float((primary["x1"] + primary["x2"]) / 2.0)
            driver_message.cy = float((primary["y1"] + primary["y2"]) / 2.0)
        else:
            driver_message.cx = 0.0
            driver_message.cy = 0.0

        driver_message.frame_w = int(result.width)
        driver_message.frame_h = int(result.height)
        self._driver_detection_publisher.publish(driver_message)

    def _publish_status(self) -> None:
        status_message = YoloStatus()
        status_message.enabled = True
        status_message.model_loaded = self._model_loaded
        status_message.conf_threshold = float(CONFIDENCE)
        status_message.last_error = str(self._last_error)
        self._status_publisher.publish(status_message)

        signature = (
            status_message.enabled,
            status_message.model_loaded,
            status_message.conf_threshold,
            status_message.last_error,
        )
        if signature != self._last_status_signature:
            packet = build_status_packet(
                status_message.enabled,
                status_message.model_loaded,
                status_message.conf_threshold,
                status_message.last_error,
                INPUT_IMAGE_TOPIC,
            )
            self._send_packet_async(packet)
            self._last_status_signature = signature

        self._last_status_sent_monotonic = time.monotonic()


def main() -> None:
    rclpy.init()
    node = GuiCameraYoloNode()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
