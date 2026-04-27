# JetsonThor.RosCameraBridge

This folder is for the case where ROS already provides:

- EO image topic
- IR image topic
- EO detection topic
- IR detection topic

and you only want to bridge those ROS topics into the existing GUI UDP format.

## Current role

Use this folder when another YOLO system already publishes detections such as:

- `/detections/eo`
- `/detections/ir`

and you want to forward:

- `/video/eo/preprocessed` -> GUI UDP `5000`
- `/video/ir/preprocessed` -> GUI UDP `5001`

## Not the recommended path for the current local YOLO workflow

If you want:

```text
camera -> Jetson -> local YOLO -> laptop GUI
```

use [../JetsonThor.EoTopicYoloDocker](../JetsonThor.EoTopicYoloDocker) instead.

This folder is only for bridging ROS image/detection topics that already exist.

## Files

- `Dockerfile`
- `run_camera_udp_bridge.sh`
- `app/camera_udp_bridge.py`
