# JetsonThor.EoTopicYoloDocker

This folder is the recommended path for the current EO camera workflow:

```text
EO camera topic
-> JetsonThor.EoTopicYoloDocker
-> YOLO inference on Jetson
-> GUI UDP 5000
-> BroadcastControl.App on laptop
```

It does not rely on an external `/detections/eo` publisher from another computer.
Jetson runs YOLO locally and sends GUI-compatible `image + DETS + STAT` packets.

## Default input/output

- Input image topic: `/video/eo/preprocessed`
- GUI UDP port: `5000`
- Optional ROS output topic: `/yolo/eo/image_raw`

## Files

- `Dockerfile`
- `run_eo_topic_yolo.sh`
- `app/eo_topic_yolo_bridge.py`

## Recommended Jetson flow

1. Start the camera and preprocess pipeline on Jetson.
2. Run this Docker container for local YOLO inference.
3. Receive the EO video and boxes on the laptop GUI.

## Example run

```bash
cd ~/LIG_DNA_GUI/JetsonThor.EoTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
YOLO_DEVICE=0 \
YOLO_HALF=true \
METRICS_LOG_INTERVAL=1.0 \
bash ./run_eo_topic_yolo.sh --build
```

## Important environment variables

| Name | Default | Meaning |
|---|---|---|
| `GUI_HOST` | `192.168.1.94` | Laptop GUI IP |
| `GUI_PORT` | `5000` | EO GUI UDP port |
| `INPUT_IMAGE_TOPIC` | `/video/eo/preprocessed` | EO image input topic |
| `OUTPUT_IMAGE_TOPIC` | `/yolo/eo/image_raw` | Optional YOLO output topic |
| `PUBLISH_OUTPUT_TOPIC` | `true` | Whether to republish YOLO output image topic |
| `CONFIDENCE` | `0.60` | YOLO confidence threshold |
| `INFERENCE_SIZE` | `640` | YOLO input size |
| `STREAM_MAX_WIDTH` | `854` | Maximum GUI stream width |
| `STREAM_MAX_HEIGHT` | `480` | Maximum GUI stream height |
| `JPEG_QUALITY` | `45` | GUI stream JPEG quality |
| `YOLO_DEVICE` | `auto` | `0` when CUDA is available, otherwise `cpu` |
| `YOLO_HALF` | `true` | Enable half precision |
| `METRICS_LOG_INTERVAL` | `1.0` | Metrics log interval in seconds |

## Expected logs

```text
Input image topic: /video/eo/preprocessed
Streaming GUI packets to 192.168.1.94:5000
YOLO device: 0 (cuda_available=True)
YOLO half precision: True
First EO camera topic frame received.
First EO frame sent to GUI.
```
