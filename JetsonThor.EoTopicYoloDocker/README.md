# JetsonThor.EoTopicYoloDocker

EO 카메라 토픽을 Jetson에서 받아 YOLO를 수행한 뒤, GUI가 이미 이해하는 UDP 패킷 형식으로 EO 영상과 바운딩 박스를 보내는 전용 폴더입니다.

흐름:

```text
/video/eo/preprocessed
-> JetsonThor.EoTopicYoloDocker
-> YOLO inference on Jetson
-> /yolo/eo/image_raw (optional)
-> GUI UDP 5000 (image + DETS + STAT)
```

기본 입력 토픽:
- `/video/eo/preprocessed`

기본 출력:
- GUI `5000`
- optional ROS topic `/yolo/eo/image_raw`

포함 파일:
- `Dockerfile`
- `run_eo_topic_yolo.sh`
- `app/eo_topic_yolo_bridge.py`

실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.EoTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
YOLO_DEVICE=0 \
YOLO_HALF=true \
METRICS_LOG_INTERVAL=1.0 \
bash ./run_eo_topic_yolo.sh --build
```

주요 환경 변수:

| 이름 | 기본값 | 설명 |
|---|---|---|
| `GUI_HOST` | `192.168.1.94` | GUI PC IP |
| `GUI_PORT` | `5000` | EO GUI UDP 포트 |
| `INPUT_IMAGE_TOPIC` | `/video/eo/preprocessed` | EO 카메라 입력 토픽 |
| `OUTPUT_IMAGE_TOPIC` | `/yolo/eo/image_raw` | YOLO 처리 후 재발행 토픽 |
| `PUBLISH_OUTPUT_TOPIC` | `true` | 출력 토픽 재발행 여부 |
| `CONFIDENCE` | `0.60` | YOLO confidence |
| `INFERENCE_SIZE` | `640` | YOLO 입력 크기 |
| `STREAM_MAX_WIDTH` | `854` | GUI 송출 최대 너비 |
| `STREAM_MAX_HEIGHT` | `480` | GUI 송출 최대 높이 |
| `JPEG_QUALITY` | `45` | GUI 송출 JPEG 품질 |
| `YOLO_DEVICE` | `auto` | 비워두면 CUDA 가능 시 `0`, 아니면 `cpu` |
| `YOLO_HALF` | `true` | half precision 사용 여부 |
| `METRICS_LOG_INTERVAL` | `1.0` | VSCode 터미널 metrics 출력 주기(초) |

정상 로그 예시:

```text
Input image topic: /video/eo/preprocessed
Output image topic: /yolo/eo/image_raw
Streaming GUI packets to 192.168.1.94:5000
YOLO device: 0 (cuda_available=True)
YOLO half precision: True
First EO camera topic frame received.
First EO frame sent to GUI.
[metrics] input_fps=14.2 gui_fps=13.8 infer_ms=28.4 frame=1280x720 gpu_alloc_mb=812.4 gpu_reserved_mb=1024.0
```
