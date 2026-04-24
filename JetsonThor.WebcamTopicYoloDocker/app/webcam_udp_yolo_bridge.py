#!/usr/bin/env python3
"""Receive laptop webcam UDP frames, run YOLO, and forward GUI packets."""

from __future__ import annotations

import json
import os
import socket
import struct
import time

import cv2
import numpy as np
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


GUI_HOST = os.getenv("GUI_HOST", "192.168.1.94")
GUI_PORT = getenv_int("GUI_PORT", 5000)
LISTEN_PORT = getenv_int("LISTEN_PORT", 5600)
MODEL_PATH = os.getenv("MODEL_PATH", "/opt/models/yolo11s.pt").strip()
CONFIDENCE = getenv_float("CONFIDENCE", 0.60)
INFERENCE_SIZE = getenv_int("INFERENCE_SIZE", 640)
STREAM_MAX_WIDTH = max(320, getenv_int("STREAM_MAX_WIDTH", 854))
STREAM_MAX_HEIGHT = max(240, getenv_int("STREAM_MAX_HEIGHT", 480))
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 45)
MAX_UDP_BYTES = getenv_int("MAX_UDP_BYTES", 55000)
RECEIVE_BUFFER_BYTES = max(1024 * 1024, getenv_int("RECEIVE_BUFFER_BYTES", 8 * 1024 * 1024))


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


def decode_laptop_packet(packet: bytes) -> tuple[int, int, int, np.ndarray]:
    if len(packet) <= LEGACY_IMAGE_HEADER_SIZE:
        raise ValueError("UDP packet is smaller than the webcam image header")

    header = packet[:LEGACY_IMAGE_HEADER_SIZE]
    frame_stamp_ns, frame_index, image_length, width, height = struct.unpack("!QIIHH", header)
    jpeg_bytes = packet[LEGACY_IMAGE_HEADER_SIZE:]
    if image_length != len(jpeg_bytes):
        raise ValueError(f"JPEG byte length mismatch: declared={image_length}, actual={len(jpeg_bytes)}")

    decoded = cv2.imdecode(np.frombuffer(jpeg_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
    if decoded is None:
        raise ValueError("Failed to decode JPEG from laptop webcam packet")

    if width > 0 and height > 0 and (decoded.shape[1] != width or decoded.shape[0] != height):
        decoded = cv2.resize(decoded, (width, height), interpolation=cv2.INTER_LINEAR)

    return frame_stamp_ns, frame_index, image_length, decoded


def main() -> None:
    print(f"Listening for laptop webcam UDP on 0.0.0.0:{LISTEN_PORT}")
    print(f"Streaming GUI packets to {GUI_HOST}:{GUI_PORT}")
    print(f"Using model: {MODEL_PATH}")

    model = YOLO(MODEL_PATH)
    model_loaded = True
    input_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    input_sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, RECEIVE_BUFFER_BYTES)
    input_sock.bind(("0.0.0.0", LISTEN_PORT))

    output_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    first_input_logged = False
    first_gui_logged = False

    while True:
        packet, remote = input_sock.recvfrom(MAX_UDP_BYTES + 4096)
        try:
            frame_stamp_ns, frame_index, _, frame = decode_laptop_packet(packet)
            if not first_input_logged:
                print(f"First laptop webcam packet received from {remote[0]}:{remote[1]}")
                first_input_logged = True

            results = model.predict(frame, conf=CONFIDENCE, imgsz=INFERENCE_SIZE, verbose=False)
            detections = extract_detections(results, frame.shape[1], frame.shape[0])

            stream_frame = fit_frame_to_stream(frame)
            encoded_bytes = encode_frame_for_udp(stream_frame)
            if encoded_bytes is None:
                raise RuntimeError("JPEG encode failed within the UDP payload limit")

            image_packet = build_image_packet(
                encoded_bytes,
                stream_frame.shape[1],
                stream_frame.shape[0],
                frame_index,
                frame_stamp_ns or time.time_ns(),
            )
            detection_packet = build_detection_packet(
                frame_stamp_ns or time.time_ns(),
                frame_index,
                frame.shape[1],
                frame.shape[0],
                detections,
            )
            status_packet = build_status_packet(
                enabled=True,
                model_loaded=model_loaded,
                conf_threshold=CONFIDENCE,
                last_error="",
                source=f"udp:{LISTEN_PORT}",
                frame_stamp_ns=frame_stamp_ns or time.time_ns(),
                frame_index=frame_index,
            )

            output_sock.sendto(image_packet, (GUI_HOST, GUI_PORT))
            output_sock.sendto(detection_packet, (GUI_HOST, GUI_PORT))
            output_sock.sendto(status_packet, (GUI_HOST, GUI_PORT))

            if not first_gui_logged:
                print("First EO frame sent to GUI.")
                first_gui_logged = True
        except Exception as exc:
            print(f"Failed to process laptop webcam UDP frame: {exc}")
            status_packet = build_status_packet(
                enabled=True,
                model_loaded=model_loaded,
                conf_threshold=CONFIDENCE,
                last_error=str(exc),
                source=f"udp:{LISTEN_PORT}",
                frame_stamp_ns=time.time_ns(),
                frame_index=0,
            )
            output_sock.sendto(status_packet, (GUI_HOST, GUI_PORT))


if __name__ == "__main__":
    main()
