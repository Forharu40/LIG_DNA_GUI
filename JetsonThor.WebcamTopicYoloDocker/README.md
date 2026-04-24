# JetsonThor.WebcamTopicYoloDocker

노트북 웹캠 영상을 Jetson에 보내고, Jetson에서 기존 MEVA 데모와 같은 YOLO + GUI UDP 패킷 형식으로 처리하는 실험용 폴더입니다.

이 폴더는 두 가지 입력 방식을 지원합니다.

1. ROS2 토픽 입력
2. 노트북 Windows CMD용 UDP 입력

이 폴더는 다음 흐름을 대상으로 합니다.

```text
Laptop webcam
-> /video/eo/preprocessed
-> JetsonThor.WebcamTopicYoloDocker
-> YOLO inference on Jetson
-> /yolo/eo/image_raw (optional republish)
-> GUI UDP 5000 (image + DETS + STAT)
```

## 재사용한 요소

- 입력 토픽 이름: `/video/eo/preprocessed`
- GUI 패킷 형식:
  - `20-byte JPEG image packet`
  - `DETS + JSON`
  - `STAT + JSON`
- YOLO 런타임과 모델 준비 방식:
  - 기존 MEVA 데모 Docker 구조를 재사용

## 포함 파일

- `Dockerfile`
- `run_webcam_topic_yolo.sh`
- `run_webcam_udp_yolo.sh`
- `app/webcam_topic_yolo_bridge.py`
- `app/webcam_udp_yolo_bridge.py`

## 1. ROS2 토픽 입력 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.WebcamTopicYoloDocker
bash ./run_webcam_topic_yolo.sh --build
```

## 2. Windows CMD UDP 입력 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.WebcamTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
LISTEN_PORT=5600 \
bash ./run_webcam_udp_yolo.sh --build
```

## 주요 환경 변수

| 이름 | 기본값 | 설명 |
|---|---|---|
| `GUI_HOST` | `192.168.1.94` | GUI PC IP |
| `GUI_PORT` | `5000` | EO GUI UDP 포트 |
| `INPUT_IMAGE_TOPIC` | `/video/eo/preprocessed` | ROS2 입력 토픽 |
| `OUTPUT_IMAGE_TOPIC` | `/yolo/eo/image_raw` | YOLO 처리 후 재발행할 영상 토픽 |
| `PUBLISH_OUTPUT_TOPIC` | `true` | `/yolo/eo/image_raw` 재발행 여부 |
| `LISTEN_PORT` | `5600` | 노트북 UDP 웹캠 입력 포트 |
| `CONFIDENCE` | `0.60` | YOLO confidence |
| `INFERENCE_SIZE` | `640` | YOLO inference size |
| `STREAM_MAX_WIDTH` | `854` | GUI 송출 폭 제한 |
| `STREAM_MAX_HEIGHT` | `480` | GUI 송출 높이 제한 |
| `JPEG_QUALITY` | `45` | GUI JPEG 품질 |
| `MODEL_PATH` | `/opt/models/yolo11s.pt` | YOLO 모델 경로 |
| `ROS_DOMAIN_ID` | 없음 | 노트북과 Jetson이 같아야 함 |
| `RMW_IMPLEMENTATION` | 없음 | 필요 시 DDS 구현 지정 |

## 로그 기준

정상이라면 Jetson 컨테이너 로그에 아래와 비슷한 흐름이 보입니다.

```text
Input image topic: /video/eo/preprocessed
Output image topic: /yolo/eo/image_raw
Streaming GUI packets to 192.168.1.94:5000
First webcam topic frame received.
First EO frame sent to GUI.
```

UDP 입력 모드에서는 이런 흐름이 보입니다.

```text
Listening for laptop webcam UDP on 0.0.0.0:5600
Streaming GUI packets to 192.168.1.94:5000
First laptop webcam packet received from ...
First EO frame sent to GUI.
```

## 주의

- 이 실험 버전은 `sensor_msgs/msg/Image`만 사용해서 `minji` workspace 의존을 줄였습니다.
- ROS2 커스텀 `sentinel_interfaces` 메시지는 사용하지 않습니다.
- 바운딩 박스는 Jetson이 `DETS + JSON` 패킷으로 보내고, GUI가 기존 방식대로 화면 위에 그립니다.
