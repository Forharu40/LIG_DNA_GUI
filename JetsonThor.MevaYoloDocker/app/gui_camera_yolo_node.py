#!/usr/bin/env python3
"""Standalone gui_camera YOLO entrypoint.

This launcher intentionally avoids shared ROS2 workspaces and shared containers.
It reuses the proven live-camera YOLO sender implementation, but runs as the
dedicated gui_camera image/container.
"""

from stream_live_camera_yolo import main


if __name__ == "__main__":
    main()
