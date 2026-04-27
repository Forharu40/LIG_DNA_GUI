# GUI Camera YOLO Guide

This guide describes the current recommended path for showing EO camera video and YOLO boxes on the laptop GUI.

## Recommended path

```text
camera
-> Jetson
-> local YOLO on Jetson
-> GUI UDP packets
-> BroadcastControl.App on laptop
```

Use `JetsonThor.EoTopicYoloDocker` for this path.

Do not use `JetsonThor.RosCameraBridge` unless detections are already being published by another ROS YOLO system.

## Jetson terminal 1: camera and preprocess

If your camera pipeline already publishes `/video/eo/preprocessed`, keep that running.

Typical host setup:

```bash
export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
export ROS_DOMAIN_ID=0
source /opt/ros/jazzy/setup.bash
source ~/gui_camera_ws/install/local_setup.bash
```

Then start the camera/preprocess path that provides:

- `/camera/eo`
- `/video/eo/preprocessed`

## Jetson terminal 2: local YOLO and GUI forwarder

```bash
cd ~/LIG_DNA_GUI/JetsonThor.EoTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
YOLO_DEVICE=0 \
YOLO_HALF=true \
METRICS_LOG_INTERVAL=1.0 \
bash ./run_eo_topic_yolo.sh --build
```

## What this container does

- subscribes to `/video/eo/preprocessed`
- runs YOLO on Jetson GPU when available
- sends GUI-compatible EO image packets
- sends GUI-compatible EO detection packets
- optionally republishes `/yolo/eo/image_raw`

## Expected logs

```text
Input image topic: /video/eo/preprocessed
Streaming GUI packets to 192.168.1.94:5000
YOLO device: 0 (cuda_available=True)
YOLO half precision: True
First EO camera topic frame received.
First EO frame sent to GUI.
```

## When to use RosCameraBridge instead

Use `JetsonThor.RosCameraBridge` only when:

- another YOLO system already publishes `/detections/eo` or `/detections/ir`
- you do not want JetsonThor.EoTopicYoloDocker to run YOLO itself
- you only need ROS topics converted into GUI UDP packets
