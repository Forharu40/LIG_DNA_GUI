# GUI_camera Standalone YOLO Guide

이 문서는 `gui_camera`를 **완전히 분리된 독립 컨테이너**로 운용하는 방법을 설명한다.

이제 `gui_camera`는 아래를 사용하지 않는다.

- `minji-perception`
- 공유 ROS2 workspace
- 기존 `/video/eo/preprocessed` 토픽
- 다른 사람이 사용 중인 Jetson ROS2 launch

즉, `gui_camera`는 자기 컨테이너 안에서:

1. 카메라를 직접 열고
2. YOLO를 직접 수행하고
3. JPEG / DETS / STAT UDP 패킷을 GUI로 직접 보낸다

---

## 1. 쉽게 이해하는 Node / Topic / Packet

### Node

`node`는 하나의 실행 프로그램이다.

예:

- 카메라 수신 프로그램
- YOLO 추론 프로그램
- GUI 송출 프로그램

지금 `gui_camera`는 이 세 역할을 **한 컨테이너 안에서 같이 수행하는 독립 실행 프로그램**이다.

### Topic

`topic`은 ROS2 안에서 프로그램끼리 데이터를 주고받는 길이다.

하지만 지금 새 `gui_camera` 방식은 **ROS2 topic을 아예 사용하지 않는다.**

즉, 이번 방식에서는 topic 의존이 없다.

### Packet

`packet`은 Jetson과 GUI PC 사이를 오가는 실제 UDP 데이터다.

지금 `gui_camera`는 아래 세 packet을 직접 보낸다.

- 영상 packet
- detection packet
- status packet

짧게 정리하면:

- `node` = 프로그램
- `topic` = ROS2 내부 통신선
- `packet` = Jetson과 GUI 사이 UDP 데이터

이번 `gui_camera`는:

```text
카메라 -> gui_camera 컨테이너 -> UDP packet -> GUI
```

구조다.

---

## 2. 왜 완전 분리로 바꿨는가

기존 방식은 아래 문제가 있었다.

- 다른 사람이 사용하는 `minji` workspace를 건드릴 수 있음
- 공유 launch, shared topic, shared package 상태에 영향 받을 수 있음
- 잘못하면 다른 사람 컨테이너나 workspace에 간섭하게 됨

이번 변경의 목표는:

- 다른 사람 컨테이너와 완전히 분리
- 다른 사람 workspace와 완전히 분리
- 내 전용 image / container로만 실행
- YOLO와 GUI 송출을 한 컨테이너에서 처리

---

## 3. 새 구조

이제 `gui_camera`는 아래 구조다.

```text
EO/IR camera device
-> gui_camera container
   -> camera capture
   -> YOLO inference
   -> JPEG encode
   -> UDP image packet
   -> UDP detection packet
   -> UDP status packet
-> BroadcastControl GUI
```

즉, Jetson의 카메라 장치를 컨테이너에 직접 연결해서 사용한다.

---

## 4. GUI로 보내는 packet 형식

### 4-1. 영상 packet

구조:

```text
20-byte header + JPEG bytes
```

헤더 형식:

```text
!QIIHH
```

필드:

- `stampNs`
- `frameId`
- `jpegLength`
- `width`
- `height`

### 4-2. Detection packet

구조:

```text
DETS + compact JSON
```

JSON 주요 필드:

- `stampNs`
- `frameId`
- `width`
- `height`
- `detections[]`

### 4-3. Status packet

구조:

```text
STAT + compact JSON
```

JSON 주요 필드:

- `enabled`
- `modelLoaded`
- `confThreshold`
- `lastError`
- `source`
- `stampNs`
- `frameId`

즉 GUI는 ROS2 없이도 이 UDP packet만 받으면 화면을 띄울 수 있다.

---

## 5. 새 이미지와 컨테이너

### 이미지

- 사용자 개념 이름: `GUI_camera`
- 실제 Docker tag: `gui_camera`

### 컨테이너

- 기본 컨테이너 이름: `gui_camera_node`

### 베이스 이미지

이제 베이스는 완전히 독립적으로:

- `ultralytics/ultralytics:latest-nvidia-arm64`

를 사용한다.

즉 더 이상 `minji-perception`을 부모로 쓰지 않는다.

---

## 6. 포함된 파일

- `JetsonThor.MevaYoloDocker/Dockerfile.gui_camera`
- `JetsonThor.MevaYoloDocker/app/gui_camera_yolo_node.py`
- `JetsonThor.MevaYoloDocker/app/stream_live_camera_yolo.py`
- `JetsonThor.MevaYoloDocker/run_gui_camera_yolo_node.sh`

설명:

- `stream_live_camera_yolo.py`
  - 실제 카메라 캡처, YOLO 추론, JPEG/UDP 송출 구현
- `gui_camera_yolo_node.py`
  - `gui_camera` 전용 entrypoint
- `Dockerfile.gui_camera`
  - 완전 독립 이미지 정의
- `run_gui_camera_yolo_node.sh`
  - 전용 컨테이너 실행 스크립트

---

## 7. 실행 방법

Jetson에서:

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_23_ver2_gui-camera-node
git pull
```

그 다음 실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_gui_camera_yolo_node.sh --build
```

---

## 8. 기본값

- image name: `gui_camera`
- container name: `gui_camera_node`
- base image: `ultralytics/ultralytics:latest-nvidia-arm64`
- model: `yolo11s.pt`
- camera source: `/dev/video0`
- camera device mapping: `/dev/video0`
- GUI host: `192.168.1.94`
- GUI port: `5000`

---

## 9. 자주 쓰는 실행 예시

### 기본 EO 카메라 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_gui_camera_yolo_node.sh --build
```

### 장치를 명시해서 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
IMAGE_NAME=GUI_camera \
CONTAINER_NAME=gui_camera_node \
CAMERA_SOURCE=/dev/video0 \
CAMERA_DEVICE=/dev/video0 \
CAMERA_BACKEND=v4l2 \
CAMERA_WIDTH=1280 \
CAMERA_HEIGHT=720 \
CAMERA_FPS=30 \
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
bash ./run_gui_camera_yolo_node.sh --build
```

### CSI / GStreamer source 예시

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
CAMERA_SOURCE="nvarguscamerasrc ! video/x-raw(memory:NVMM), width=1280, height=720, framerate=30/1 ! nvvidconv ! video/x-raw, format=BGRx ! videoconvert ! video/x-raw, format=BGR ! appsink drop=true max-buffers=1" \
CAMERA_DEVICE= \
CAMERA_BACKEND=gstreamer \
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
bash ./run_gui_camera_yolo_node.sh --build
```

---

## 10. 이 방식에서 중요한 점

이 방식은 완전히 분리되어 있으므로:

- 다른 사람 ROS2 launch를 안 건드린다
- 다른 사람 workspace를 안 건드린다
- 다른 사람 container를 안 건드린다

대신 아래는 필요하다.

- Jetson에서 카메라 장치가 실제로 보여야 함
  - 예: `/dev/video0`
- 또는 GStreamer source가 실제로 열려야 함
- GUI PC IP가 맞아야 함
- GUI 프로그램이 켜져 있어야 함

즉 이 방식의 핵심 의존성은 **ROS2 topic이 아니라 카메라 장치 자체**다.

---

## 11. 지금 이 방식으로 바로 GUI에서 보이는 조건

조건이 맞으면, 이 방식은 ROS2 launch 없이도 바로 GUI에 영상을 보낼 수 있다.

필요 조건:

1. 카메라가 Jetson에 연결되어 있음
2. `CAMERA_SOURCE`가 실제 장치를 가리킴
3. 컨테이너가 카메라 장치를 열 수 있음
4. GUI가 켜져 있음

즉:

```text
카메라 on
+ gui_camera container on
+ GUI on
= 영상 표시 가능
```

---

## 12. 요약

이번 `gui_camera`는 이제 아래 방식이다.

```text
공유 ROS2 시스템 사용 X
공유 workspace 사용 X
공유 topic 사용 X
```

대신:

```text
카메라 직접 열기
-> YOLO 직접 수행
-> GUI UDP 직접 송출
```

즉, **완전히 독립된 내 전용 카메라+YOLO+GUI 송출 컨테이너**다.
