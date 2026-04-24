#!/usr/bin/env python3
"""Send laptop webcam frames to Jetson over UDP without ROS2."""

from __future__ import annotations

import os
import socket
import struct
import time

import cv2


def getenv_int(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except ValueError:
        return default


JETSON_HOST = os.getenv("JETSON_HOST", "192.168.1.50")
JETSON_PORT = getenv_int("JETSON_PORT", 5600)
CAMERA_INDEX = getenv_int("CAMERA_INDEX", 0)
FRAME_WIDTH = getenv_int("FRAME_WIDTH", 1280)
FRAME_HEIGHT = getenv_int("FRAME_HEIGHT", 720)
TARGET_FPS = max(1, getenv_int("TARGET_FPS", 10))
JPEG_QUALITY = getenv_int("JPEG_QUALITY", 70)
MAX_UDP_BYTES = getenv_int("MAX_UDP_BYTES", 60000)


def build_packet(encoded_bytes: bytes, frame_index: int, width: int, height: int) -> bytes:
    header = struct.pack(
        "!QIIHH",
        time.time_ns(),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, width),
        max(0, height),
    )
    return header + encoded_bytes


def main() -> None:
    capture = cv2.VideoCapture(CAMERA_INDEX)
    capture.set(cv2.CAP_PROP_FRAME_WIDTH, FRAME_WIDTH)
    capture.set(cv2.CAP_PROP_FRAME_HEIGHT, FRAME_HEIGHT)
    capture.set(cv2.CAP_PROP_FPS, TARGET_FPS)

    if not capture.isOpened():
        raise RuntimeError(f"Could not open laptop webcam index {CAMERA_INDEX}")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    frame_interval_seconds = 1.0 / float(TARGET_FPS)
    frame_index = 0
    first_logged = False

    print(f"Sending laptop webcam UDP to {JETSON_HOST}:{JETSON_PORT}")
    print(f"Camera index: {CAMERA_INDEX}")
    print(f"Target size: {FRAME_WIDTH}x{FRAME_HEIGHT}")
    print(f"Target FPS: {TARGET_FPS}")

    try:
        while True:
            started_at = time.perf_counter()
            ok, frame = capture.read()
            if not ok or frame is None:
                print("Failed to read a frame from the laptop webcam.")
                time.sleep(0.1)
                continue

            frame = cv2.flip(frame, 1)

            if frame.shape[1] != FRAME_WIDTH or frame.shape[0] != FRAME_HEIGHT:
                frame = cv2.resize(frame, (FRAME_WIDTH, FRAME_HEIGHT), interpolation=cv2.INTER_AREA)

            ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY])
            if not ok:
                print("Failed to JPEG-encode a webcam frame.")
                continue

            packet = build_packet(encoded.tobytes(), frame_index, frame.shape[1], frame.shape[0])
            if len(packet) > MAX_UDP_BYTES:
                print(f"UDP packet too large ({len(packet)} bytes). Lower JPEG_QUALITY or resolution.")
                continue

            sock.sendto(packet, (JETSON_HOST, JETSON_PORT))
            frame_index += 1
            if not first_logged:
                print("First webcam UDP frame sent.")
                first_logged = True

            elapsed = time.perf_counter() - started_at
            remaining = frame_interval_seconds - elapsed
            if remaining > 0:
                time.sleep(remaining)
    finally:
        capture.release()
        sock.close()


if __name__ == "__main__":
    main()
