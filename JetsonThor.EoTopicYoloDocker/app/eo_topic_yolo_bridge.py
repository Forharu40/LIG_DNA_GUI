#!/usr/bin/env python3
"""Run YOLO on an EO camera ROS topic and forward GUI-compatible UDP packets."""

from __future__ import annotations

from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
import json
import os
import socket
import struct
import time

import cv2
import numpy as np
import rclpy
import torch
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image
from ultralytics import YOLO


LEGACY_IMAGE_HEADER_SIZE = 20
DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"


@dataclass(frozen=True)
class DetectionResult:
    width: int
    height: int
    detections: list[dict]
    inference_ms: float


@dataclass(frozen=True)
class EncodedFrame:
    encoded_bytes: bytes
    width: int
    height: int
    detection_width: int
    detection_height: int
    detections: list[dict]
    frame_index: int
    frame_stamp_ns: int
    encode_ms: float


@dataclass(frozen=True)
class EncodeRequest:
    frame: np.ndarray
    detection_width: int
    detection_height: int
    detections: list[dict]
    frame_index: int
    frame_stamp_ns: int


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


def resolve_yolo_device() -> str:
    requested = os.getenv("YOLO_DEVICE", "").strip()
    if requested:
        return requested
    return "0" if torch.cuda.is_available() else "cpu"


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
YOLO_DEVICE = resolve_yolo_device()
YOLO_HALF = getenv_bool("YOLO_HALF", YOLO_DEVICE != "cpu")
METRICS_LOG_INTERVAL = max(0.2, getenv_float("METRICS_LOG_INTERVAL", 1.0))
HORIZONTAL_FLIP = getenv_bool("HORIZONTAL_FLIP", True)


def resolve_torch_device_index() -> int | None:
    if not torch.cuda.is_available() or YOLO_DEVICE == "cpu":
        return None
    if YOLO_DEVICE.isdigit():
        return int(YOLO_DEVICE)
    if YOLO_DEVICE.startswith("cuda:"):
        try:
            return int(YOLO_DEVICE.split(":", 1)[1])
        except ValueError:
            return 0
    if YOLO_DEVICE == "cuda":
        return 0
    return torch.cuda.current_device()


def get_gpu_memory_metrics_mb() -> tuple[float, float]:
    device_index = resolve_torch_device_index()
    if device_index is None:
        return 0.0, 0.0
    allocated = torch.cuda.memory_allocated(device_index) / (1024 * 1024)
    reserved = torch.cuda.memory_reserved(device_index) / (1024 * 1024)
    return allocated, reserved


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


def detect_objects_for_packet(model: YOLO, frame: np.ndarray) -> DetectionResult:
    started_at = time.perf_counter()
    results = model.predict(
        frame,
        conf=CONFIDENCE,
        imgsz=INFERENCE_SIZE,
        verbose=False,
        device=YOLO_DEVICE,
        half=YOLO_HALF,
    )
    inference_ms = (time.perf_counter() - started_at) * 1000.0
    return DetectionResult(
        width=frame.shape[1],
        height=frame.shape[0],
        detections=extract_detections(results, frame.shape[1], frame.shape[0]),
        inference_ms=inference_ms,
    )


def encode_frame_for_packet(
    frame: np.ndarray,
    detection_width: int,
    detection_height: int,
    detections: list[dict],
    frame_index: int,
    frame_stamp_ns: int,
) -> EncodedFrame | None:
    started_at = time.perf_counter()
    encoded_bytes = encode_frame_for_udp(frame)
    if encoded_bytes is None:
        return None
    encode_ms = (time.perf_counter() - started_at) * 1000.0

    return EncodedFrame(
        encoded_bytes=encoded_bytes,
        width=frame.shape[1],
        height=frame.shape[0],
        detection_width=detection_width,
        detection_height=detection_height,
        detections=[dict(detection) for detection in detections],
        frame_index=frame_index,
        frame_stamp_ns=frame_stamp_ns,
        encode_ms=encode_ms,
    )


def send_encoded_frame(
    sock: socket.socket,
    encoded_frame: EncodedFrame,
    gui_host: str,
    gui_port: int,
    source: str,
    model_loaded: bool,
) -> tuple[float, int, int, int]:
    started_at = time.perf_counter()
    image_packet = build_image_packet(
        encoded_frame.encoded_bytes,
        encoded_frame.width,
        encoded_frame.height,
        encoded_frame.frame_index,
        encoded_frame.frame_stamp_ns,
    )
    detection_packet = build_detection_packet(
        encoded_frame.frame_stamp_ns,
        encoded_frame.frame_index,
        encoded_frame.detection_width,
        encoded_frame.detection_height,
        encoded_frame.detections,
    )
    status_packet = build_status_packet(
        enabled=True,
        model_loaded=model_loaded,
        conf_threshold=CONFIDENCE,
        last_error="",
        source=source,
        frame_stamp_ns=encoded_frame.frame_stamp_ns,
        frame_index=encoded_frame.frame_index,
    )

    sock.sendto(image_packet, (gui_host, gui_port))
    sock.sendto(detection_packet, (gui_host, gui_port))
    sock.sendto(status_packet, (gui_host, gui_port))
    send_ms = (time.perf_counter() - started_at) * 1000.0
    return send_ms, len(image_packet), len(detection_packet), len(status_packet)


class EoTopicYoloBridge(Node):
    def __init__(self) -> None:
        super().__init__("eo_topic_yolo_bridge")
        self._frame_index = 0
        self._model_loaded = False
        self._last_error = ""
        self._first_frame_logged = False
        self._first_gui_frame_logged = False
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        torch.backends.cudnn.benchmark = True
        self._model = YOLO(MODEL_PATH)
        self._model_loaded = True
        self._output_publisher = None
        self._detection_executor = ThreadPoolExecutor(max_workers=1)
        self._encode_executor = ThreadPoolExecutor(max_workers=1)
        self._send_executor = ThreadPoolExecutor(max_workers=1)
        self._latest_detections: list[dict] = []
        self._latest_detection_width = 0
        self._latest_detection_height = 0
        self._latest_inference_ms = 0.0
        self._latest_encode_ms = 0.0
        self._latest_send_ms = 0.0
        self._latest_frame_width = 0
        self._latest_frame_height = 0
        self._latest_image_packet_bytes = 0
        self._latest_detection_packet_bytes = 0
        self._latest_status_packet_bytes = 0
        self._input_frames_since_log = 0
        self._gui_frames_since_log = 0
        self._last_metrics_log_at = time.monotonic()
        self._pending_detection: Future[DetectionResult] | None = None
        self._pending_encoded_frame: Future[EncodedFrame | None] | None = None
        self._pending_send: Future[tuple[float, int, int, int]] | None = None
        self._queued_detection_frame: np.ndarray | None = None
        self._queued_encode_request: EncodeRequest | None = None
        self._queued_encoded_frame: EncodedFrame | None = None

        if PUBLISH_OUTPUT_TOPIC:
            self._output_publisher = self.create_publisher(Image, OUTPUT_IMAGE_TOPIC, qos_profile_sensor_data)

        self.create_subscription(Image, INPUT_IMAGE_TOPIC, self._on_image, qos_profile_sensor_data)

        self.get_logger().info(f"Input image topic: {INPUT_IMAGE_TOPIC}")
        if PUBLISH_OUTPUT_TOPIC:
            self.get_logger().info(f"Output image topic: {OUTPUT_IMAGE_TOPIC}")
        self.get_logger().info(f"Streaming GUI packets to {GUI_HOST}:{GUI_PORT}")
        self.get_logger().info(f"Using model: {MODEL_PATH}")
        self.get_logger().info(f"YOLO device: {YOLO_DEVICE} (cuda_available={torch.cuda.is_available()})")
        self.get_logger().info(f"YOLO half precision: {YOLO_HALF}")
        self.get_logger().info(f"Horizontal flip: {HORIZONTAL_FLIP}")
        self.get_logger().info(f"Metrics log interval: {METRICS_LOG_INTERVAL:.1f}s")

    def _on_image(self, message: Image) -> None:
        frame_stamp_ns = (
            int(message.header.stamp.sec) * 1_000_000_000
            + int(message.header.stamp.nanosec)
        )

        try:
            if self._pending_send is not None and self._pending_send.done():
                send_ms, image_packet_bytes, detection_packet_bytes, status_packet_bytes = self._pending_send.result()
                self._latest_send_ms = send_ms
                self._latest_image_packet_bytes = image_packet_bytes
                self._latest_detection_packet_bytes = detection_packet_bytes
                self._latest_status_packet_bytes = status_packet_bytes
                self._pending_send = None

            if self._pending_detection is not None and self._pending_detection.done():
                detection_result = self._pending_detection.result()
                self._latest_detections = detection_result.detections
                self._latest_detection_width = detection_result.width
                self._latest_detection_height = detection_result.height
                self._latest_inference_ms = detection_result.inference_ms
                self._pending_detection = None
                if self._queued_detection_frame is not None:
                    queued_detection_frame = self._queued_detection_frame
                    self._queued_detection_frame = None
                    self._pending_detection = self._detection_executor.submit(
                        detect_objects_for_packet,
                        self._model,
                        queued_detection_frame,
                    )

            if self._pending_encoded_frame is not None and self._pending_encoded_frame.done():
                encoded_frame = self._pending_encoded_frame.result()
                self._pending_encoded_frame = None
                if encoded_frame is not None:
                    self._latest_encode_ms = encoded_frame.encode_ms
                    # Keep only the newest encoded frame while a send is in flight.
                    self._queued_encoded_frame = encoded_frame
                if self._queued_encode_request is not None:
                    queued_encode_request = self._queued_encode_request
                    self._queued_encode_request = None
                    self._pending_encoded_frame = self._encode_executor.submit(
                        encode_frame_for_packet,
                        queued_encode_request.frame,
                        queued_encode_request.detection_width,
                        queued_encode_request.detection_height,
                        queued_encode_request.detections,
                        queued_encode_request.frame_index,
                        queued_encode_request.frame_stamp_ns,
                    )

            if self._pending_send is None and self._queued_encoded_frame is not None:
                encoded_frame = self._queued_encoded_frame
                self._queued_encoded_frame = None
                self._pending_send = self._send_executor.submit(
                    send_encoded_frame,
                    self._sock,
                    encoded_frame,
                    GUI_HOST,
                    GUI_PORT,
                    INPUT_IMAGE_TOPIC,
                    self._model_loaded,
                )
                self._gui_frames_since_log += 1
                if not self._first_gui_frame_logged:
                    self.get_logger().info("First EO frame sent to GUI.")
                    self._first_gui_frame_logged = True

            frame = ros_image_to_bgr(message)
            if HORIZONTAL_FLIP:
                frame = cv2.flip(frame, 1)
            self._frame_index += 1
            self._input_frames_since_log += 1
            self._latest_frame_width = frame.shape[1]
            self._latest_frame_height = frame.shape[0]

            if not self._first_frame_logged:
                self.get_logger().info("First EO camera topic frame received.")
                self._first_frame_logged = True

            if self._pending_detection is None:
                self._pending_detection = self._detection_executor.submit(
                    detect_objects_for_packet,
                    self._model,
                    frame.copy(),
                )
            else:
                self._queued_detection_frame = frame.copy()

            stream_frame = fit_frame_to_stream(frame)
            if self._pending_encoded_frame is None:
                self._pending_encoded_frame = self._encode_executor.submit(
                    encode_frame_for_packet,
                    stream_frame.copy(),
                    self._latest_detection_width or frame.shape[1],
                    self._latest_detection_height or frame.shape[0],
                    [dict(detection) for detection in self._latest_detections],
                    self._frame_index,
                    frame_stamp_ns or time.time_ns(),
                )
            else:
                self._queued_encode_request = EncodeRequest(
                    frame=stream_frame.copy(),
                    detection_width=self._latest_detection_width or frame.shape[1],
                    detection_height=self._latest_detection_height or frame.shape[0],
                    detections=[dict(detection) for detection in self._latest_detections],
                    frame_index=self._frame_index,
                    frame_stamp_ns=frame_stamp_ns or time.time_ns(),
                )

            if self._output_publisher is not None:
                self._output_publisher.publish(bgr_to_ros_image(stream_frame, message))

            now = time.monotonic()
            elapsed = now - self._last_metrics_log_at
            if elapsed >= METRICS_LOG_INTERVAL:
                input_fps = self._input_frames_since_log / elapsed
                gui_fps = self._gui_frames_since_log / elapsed
                gpu_alloc_mb, gpu_reserved_mb = get_gpu_memory_metrics_mb()
                self.get_logger().info(
                    "[metrics] "
                    f"input_fps={input_fps:.1f} "
                    f"gui_fps={gui_fps:.1f} "
                    f"infer_ms={self._latest_inference_ms:.1f} "
                    f"encode_ms={self._latest_encode_ms:.1f} "
                    f"send_ms={self._latest_send_ms:.1f} "
                    f"frame={self._latest_frame_width}x{self._latest_frame_height} "
                    f"image_kb={self._latest_image_packet_bytes / 1024:.1f} "
                    f"dets_kb={self._latest_detection_packet_bytes / 1024:.1f} "
                    f"stat_kb={self._latest_status_packet_bytes / 1024:.1f} "
                    f"gpu_alloc_mb={gpu_alloc_mb:.1f} "
                    f"gpu_reserved_mb={gpu_reserved_mb:.1f}"
                )
                self._input_frames_since_log = 0
                self._gui_frames_since_log = 0
                self._last_metrics_log_at = now
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
            self.get_logger().error(f"Failed to process EO topic frame: {exc}")

    def destroy_node(self) -> bool:
        self._detection_executor.shutdown(wait=False, cancel_futures=True)
        self._encode_executor.shutdown(wait=False, cancel_futures=True)
        self._send_executor.shutdown(wait=False, cancel_futures=True)
        self._sock.close()
        return super().destroy_node()


def main() -> None:
    rclpy.init()
    node = EoTopicYoloBridge()
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
