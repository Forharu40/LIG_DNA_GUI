#!/usr/bin/env python3
"""Publish laptop webcam frames to a ROS2 EO image topic."""

from __future__ import annotations

import os
import time

import cv2
import rclpy
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image


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


WEBCAM_TOPIC = os.getenv("WEBCAM_TOPIC", "/video/eo/preprocessed")
CAMERA_INDEX = getenv_int("CAMERA_INDEX", 0)
FRAME_WIDTH = getenv_int("FRAME_WIDTH", 1280)
FRAME_HEIGHT = getenv_int("FRAME_HEIGHT", 720)
TARGET_FPS = max(1, getenv_int("TARGET_FPS", 10))
FRAME_ID = os.getenv("FRAME_ID", "laptop_webcam_eo")
SHOW_PREVIEW = getenv_bool("SHOW_PREVIEW", True)


class WebcamTopicPublisher(Node):
    def __init__(self) -> None:
        super().__init__("laptop_webcam_topic_publisher")
        self._publisher = self.create_publisher(Image, WEBCAM_TOPIC, qos_profile_sensor_data)
        self._capture = cv2.VideoCapture(CAMERA_INDEX)
        self._capture.set(cv2.CAP_PROP_FRAME_WIDTH, FRAME_WIDTH)
        self._capture.set(cv2.CAP_PROP_FRAME_HEIGHT, FRAME_HEIGHT)
        self._capture.set(cv2.CAP_PROP_FPS, TARGET_FPS)
        self._published_frame_count = 0

        if not self._capture.isOpened():
            raise RuntimeError(f"Could not open laptop webcam index {CAMERA_INDEX}")

        self.get_logger().info(f"Publishing webcam frames to {WEBCAM_TOPIC}")
        self.get_logger().info(f"Camera index: {CAMERA_INDEX}")
        self.get_logger().info(f"Target size: {FRAME_WIDTH}x{FRAME_HEIGHT}")
        self.get_logger().info(f"Target FPS: {TARGET_FPS}")

    def run(self) -> None:
        frame_interval_seconds = 1.0 / float(TARGET_FPS)
        try:
            while rclpy.ok():
                start_time = time.perf_counter()
                ok, frame = self._capture.read()
                if not ok or frame is None:
                    self.get_logger().warning("Failed to read a frame from the laptop webcam.")
                    time.sleep(0.1)
                    continue

                frame = cv2.flip(frame, 1)

                if frame.shape[1] != FRAME_WIDTH or frame.shape[0] != FRAME_HEIGHT:
                    frame = cv2.resize(frame, (FRAME_WIDTH, FRAME_HEIGHT), interpolation=cv2.INTER_AREA)

                message = Image()
                message.header.stamp = self.get_clock().now().to_msg()
                message.header.frame_id = FRAME_ID
                message.height = int(frame.shape[0])
                message.width = int(frame.shape[1])
                message.encoding = "bgr8"
                message.is_bigendian = 0
                message.step = int(frame.shape[1] * frame.shape[2])
                message.data = frame.tobytes()
                self._publisher.publish(message)

                self._published_frame_count += 1
                if self._published_frame_count == 1:
                    self.get_logger().info("First laptop webcam frame published.")

                if SHOW_PREVIEW:
                    cv2.imshow("Laptop Webcam Publisher", frame)
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        break

                elapsed = time.perf_counter() - start_time
                remaining = frame_interval_seconds - elapsed
                if remaining > 0:
                    time.sleep(remaining)
        finally:
            self._capture.release()
            if SHOW_PREVIEW:
                cv2.destroyAllWindows()


def main() -> None:
    rclpy.init()
    node = WebcamTopicPublisher()
    try:
        node.run()
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
