# Camera UDP Bridge Guide

이 문서는 `minji-perception` 같은 기존 이미지에 의존하지 않고, 전용 Docker 이미지로 EO/IR 카메라 영상을 GUI로 UDP 송출하는 방법을 정리한 문서다.

지금 방식은 아주 단순하다.

- EO 이미지 topic: `/camera/eo`
- IR 이미지 topic: `/camera/ir`
- EO UDP port: `5000`
- IR UDP port: `5001`
- Docker 이미지: 전용 `gui_camera_bridge`

---

## 1. 역할

이 컨테이너는 YOLO를 다시 돌리지 않는다.  
ROS2 이미지 topic을 받아서 JPEG로 압축한 뒤 GUI로 UDP 전송만 한다.

구조:

```text
ROS2 /camera/eo -> JPEG UDP -> GUI port 5000
ROS2 /camera/ir -> JPEG UDP -> GUI port 5001
```

---

## 2. 포함 파일

- `JetsonThor.MevaYoloDocker/app/camera_udp_bridge.py`
- `JetsonThor.MevaYoloDocker/Dockerfile.camera_udp_bridge`
- `JetsonThor.MevaYoloDocker/run_camera_udp_bridge.sh`

설명:

- `camera_udp_bridge.py`
  - `/camera/eo`, `/camera/ir` 구독
  - 640x360 리사이즈
  - JPEG 품질 35로 압축
  - EO는 5000, IR는 5001로 전송
- `Dockerfile.camera_udp_bridge`
  - `ros:jazzy-ros-base` 기반 전용 이미지
  - `python3-opencv`, `ros-jazzy-cv-bridge` 포함
- `run_camera_udp_bridge.sh`
  - 전용 이미지를 빌드하고 컨테이너를 실행

---

## 3. 이미지와 컨테이너

기본값:

- 이미지 이름: `gui_camera_bridge`
- 컨테이너 이름: `gui_camera_bridge`
- 베이스 이미지: `ros:jazzy-ros-base`

즉 이제는:

- `minji-perception` 안 씀
- `minji` 컨테이너 안 들어갈 필요 없음
- `gui_camera_bridge` 전용 이미지 하나만 사용

---

## 4. 전송 packet 구조

영상 packet은 아래 구조다.

```text
20-byte header + JPEG bytes
```

헤더 포맷:

```text
!QIIHH
```

의미:

- `Q`: `time.time_ns()`
- `I`: 프레임 번호
- `I`: JPEG 바이트 길이
- `H`: width
- `H`: height

기본값:

- width: `640`
- height: `360`

---

## 5. 실행 전 확인

먼저 `/camera/eo`, `/camera/ir`가 실제로 살아 있어야 한다.

Jetson에서 확인:

```bash
source /opt/ros/jazzy/setup.bash
source ~/gui_camera_ws/install/local_setup.bash
ros2 topic echo /camera/eo --once
ros2 topic echo /camera/ir --once
```

여기서 이미지 메시지가 보이면 브리지 입력은 준비된 상태다.

---

## 6. 실행 방법

Jetson에서:

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_23_ver3_gui-camera-bridge
git pull

cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_camera_udp_bridge.sh --build
```

이 명령은:

1. 전용 Docker 이미지를 빌드하고
2. `--network host`로 컨테이너를 띄우고
3. ROS2 `/camera/eo`, `/camera/ir`를 구독해서
4. GUI로 UDP 영상을 보낸다

---

## 7. 환경변수 변경 예시

GUI IP를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
GUI_HOST=192.168.1.50 bash ./run_camera_udp_bridge.sh --build
```

topic 이름을 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
EO_IMAGE_TOPIC=/some/eo/topic \
IR_IMAGE_TOPIC=/some/ir/topic \
bash ./run_camera_udp_bridge.sh --build
```

해상도를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
STREAM_WIDTH=854 \
STREAM_HEIGHT=480 \
bash ./run_camera_udp_bridge.sh --build
```

ROS 2 domain을 맞춰야 하면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
ROS_DOMAIN_ID=10 bash ./run_camera_udp_bridge.sh --build
```

---

## 8. 정상 로그

정상이라면 실행 후 아래 로그가 보여야 한다.

```text
EO image topic: /camera/eo
IR image topic: /camera/ir
Streaming EO UDP packets to 192.168.1.94:5000
Streaming IR UDP packets to 192.168.1.94:5001
EO first frame sent!
IR first frame sent!
```

특히 마지막 두 줄이 중요하다.

- `EO first frame sent!`
- `IR first frame sent!`

이 로그가 보이면 실제로 GUI 쪽으로 프레임이 나가기 시작한 상태다.

---

## 9. 요약

지금 방식은 Docker는 사용하지만, `minji` 관련 이미지나 컨테이너는 쓰지 않는 전용 카메라 UDP 브리지 방식이다.

- Docker 사용
- `minji-perception` 미사용
- 전용 이미지 `gui_camera_bridge` 사용
- YOLO 재추론 없음
- `/camera/eo`, `/camera/ir`만 받아서 GUI로 UDP 송출
- EO는 `5000`
- IR는 `5001`

