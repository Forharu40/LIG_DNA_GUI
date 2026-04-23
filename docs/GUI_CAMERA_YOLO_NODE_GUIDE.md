# GUI_camera Bridge Guide

이 문서는 `gui_camera`를 현재 하드웨어 구조에 맞는 **EO GUI 브리지 전용 컨테이너**로 사용하는 방법을 설명한다.

지금 구조에서는 Jetson이 카메라를 직접 여는 것이 아니라, 기존 시스템이 이미 만들고 있는 EO 토픽을 받아 GUI UDP 패킷으로 바꿔 보내는 것이 맞다.

---

## 0. 쉽게 이해하는 Node / Topic / Packet

### Node

`node`는 하나의 실행 프로그램이다.

예:

- 카메라 영상을 받는 프로그램
- YOLO 추론을 하는 프로그램
- GUI로 패킷을 보내는 프로그램

이번 `gui_camera`는 **EO GUI 송출만 담당하는 브리지 node**다.

### Topic

`topic`은 ROS2 안에서 node끼리 데이터를 주고받는 길이다.

이번 방식에서 `gui_camera`가 받는 주요 topic은:

- `/yolo/eo/image_raw`
- `/detections/eo`
- `/yolo/eo/status`

즉, 카메라/YOLO는 기존 시스템이 만들고, `gui_camera`는 그 결과만 받는다.

### Packet

`packet`은 Jetson과 GUI PC 사이를 실제로 오가는 UDP 데이터다.

이번 `gui_camera`는 topic을 받아 아래 packet으로 바꿔 보낸다.

- 영상 packet
- detection packet
- status packet

짧게 정리하면:

- `node` = 프로그램
- `topic` = ROS2 내부 통신
- `packet` = GUI로 나가는 실제 UDP 데이터

구조는 아래와 같다.

```text
기존 EO 카메라/YOLO 시스템
-> ROS2 topics
-> gui_camera bridge
-> UDP packets
-> GUI
```

---

## 1. 왜 bridge 방식으로 바꿨는가

현재 Jetson 환경에서는:

- `/dev/video*` 가 없음
- `nvarguscamerasrc`도 `No cameras available`

즉 Jetson이 카메라를 직접 여는 독립 컨테이너 방식은 현재 장비 구조와 맞지 않는다.

반면 기존 시스템에서는 이미 아래 EO 토픽이 살아 있다.

- `/yolo/eo/image_raw`
- `/detections/eo`
- `/yolo/eo/status`

그래서 `gui_camera`는 카메라를 직접 열지 않고, **이미 존재하는 EO 토픽을 GUI로 넘기는 브리지 역할만 하는 게 가장 안전하고 현실적**이다.

---

## 2. 새 구조

```text
카메라 -> ZYBO10 -> 기존 수신/전처리/YOLO 시스템
-> /yolo/eo/image_raw
-> /detections/eo
-> /yolo/eo/status
-> gui_camera bridge container
-> GUI UDP 5000
```

즉:

- 기존 카메라/YOLO 쪽은 그대로
- `gui_camera`는 EO GUI 송출만 담당

---

## 3. gui_camera가 구독하는 토픽

기본값:

- `IMAGE_TOPIC=/yolo/eo/image_raw`
- `DETECTION_TOPIC=/detections/eo`
- `STATUS_TOPIC=/yolo/eo/status`

필요하면 환경변수로 다른 토픽으로 바꿀 수 있다.

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

주요 필드:

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

주요 필드:

- `enabled`
- `modelLoaded`
- `confThreshold`
- `lastError`
- `source`
- `stampNs`
- `frameId`

---

## 5. 새 이미지와 컨테이너

### 이미지

- 사용자 개념 이름: `GUI_camera`
- 실제 Docker tag: `gui_camera`

### 컨테이너

- 기본 컨테이너 이름: `gui_camera_bridge`

### 베이스 이미지

- `minji-perception`

이 이미지는 카메라 직접 입력용이 아니라, ROS2 topic bridge 용도다.

---

## 6. 포함된 파일

- `JetsonThor.MevaYoloDocker/Dockerfile.gui_camera`
- `JetsonThor.MevaYoloDocker/app/gui_camera_yolo_node.py`
- `JetsonThor.MevaYoloDocker/run_gui_camera_yolo_node.sh`

설명:

- `gui_camera_yolo_node.py`
  - 기존 EO ROS2 토픽을 GUI packet으로 바꾸는 브리지
- `Dockerfile.gui_camera`
  - 브리지 전용 이미지
- `run_gui_camera_yolo_node.sh`
  - 브리지 컨테이너 실행 스크립트

---

## 7. 실행 방법

Jetson에서:

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_23_ver3_gui-camera-bridge
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
- container name: `gui_camera_bridge`
- base image: `minji-perception`
- workspace: `$HOME/minji/ros2_ws`
- workspace mount mode: `ro`
- GUI host: `192.168.1.94`
- GUI port: `5000`
- image topic: `/yolo/eo/image_raw`
- detection topic: `/detections/eo`
- status topic: `/yolo/eo/status`

---

## 9. 안전한 실행 예시

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
IMAGE_NAME=GUI_camera \
CONTAINER_NAME=gui_camera_bridge \
WORKSPACE_DIR=/home/lig/minji/ros2_ws \
WORKSPACE_MOUNT_MODE=ro \
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
IMAGE_TOPIC=/yolo/eo/image_raw \
DETECTION_TOPIC=/detections/eo \
STATUS_TOPIC=/yolo/eo/status \
bash ./run_gui_camera_yolo_node.sh --build
```

이 방식은:

- 기존 카메라 장치를 직접 열지 않음
- 기존 YOLO를 다시 돌리지 않음
- 공유 workspace를 read-only로만 사용함

---

## 10. 이 방식에서 중요한 점

이 방식은 카메라 장치에 의존하지 않는다.

대신 아래가 반드시 살아 있어야 한다.

- `/yolo/eo/image_raw`
- `/detections/eo`
- `/yolo/eo/status`

즉, 기존 카메라/YOLO 시스템이 먼저 정상 동작해야 한다.

이 브리지는 그 다음 단계다.

---

## 11. 지금 바로 GUI에서 보이려면

아래가 모두 만족돼야 한다.

1. 기존 EO 카메라/YOLO 시스템이 켜져 있음
2. `/yolo/eo/image_raw` 토픽이 실제로 발행 중임
3. `gui_camera` 브리지 컨테이너가 실행 중임
4. 운용통제 PC GUI가 켜져 있음

즉:

```text
기존 EO 시스템 on
+ gui_camera bridge on
+ GUI on
= EO 영상 표시 가능
```

---

## 12. 확인 방법

Jetson에서 먼저:

```bash
source /opt/ros/jazzy/setup.bash
source /home/lig/minji/ros2_ws/install/setup.bash
ros2 topic list | grep -E "/yolo/eo/image_raw|/detections/eo|/yolo/eo/status"
ros2 topic hz /yolo/eo/image_raw
```

그 다음 bridge 실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_gui_camera_yolo_node.sh --build
```

정상이라면 bridge 로그에:

- `EO image topic: /yolo/eo/image_raw`
- `Streaming EO GUI packets to 192.168.1.94:5000`
- `First EO frame forwarded to GUI.`

가 보여야 한다.

---

## 13. 요약

이번 브랜치의 `gui_camera`는 더 이상 카메라를 직접 여는 컨테이너가 아니다.

대신:

```text
기존 EO ROS2 토픽
-> gui_camera bridge
-> GUI UDP 송출
```

을 담당하는, 현재 장비 구조에 맞는 **안전한 EO GUI 브리지 컨테이너**다.
