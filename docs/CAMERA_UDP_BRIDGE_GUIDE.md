# Camera UDP Bridge Guide

이 문서는 Docker나 `minji-perception` 이미지를 쓰지 않고, Jetson에서 ROS2 이미지 토픽을 직접 받아 GUI로 UDP 영상만 보내는 방법을 정리한 문서다.

지금 방식은 아주 단순하다.

- EO 이미지 topic: `/camera/eo`
- IR 이미지 topic: `/camera/ir`
- EO UDP port: `5000`
- IR UDP port: `5001`

---

## 1. 역할

이 스크립트는 YOLO를 다시 돌리지 않는다.  
카메라 이미지를 받아서 JPEG로 압축한 뒤 GUI로 보내는 역할만 한다.

구조:

```text
ROS2 /camera/eo -> JPEG UDP -> GUI port 5000
ROS2 /camera/ir -> JPEG UDP -> GUI port 5001
```

---

## 2. 포함 파일

- `JetsonThor.MevaYoloDocker/app/camera_udp_bridge.py`
- `JetsonThor.MevaYoloDocker/run_camera_udp_bridge.sh`

설명:

- `camera_udp_bridge.py`
  - `/camera/eo`, `/camera/ir` 구독
  - 640x360 리사이즈
  - JPEG 품질 35로 압축
  - EO는 5000, IR은 5001로 전송
- `run_camera_udp_bridge.sh`
  - ROS2 환경을 source한 뒤 브리지 파이썬 스크립트를 실행

---

## 3. 전송 packet 구조

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

## 4. 실행 전 확인

먼저 `/camera/eo`, `/camera/ir`가 실제로 살아 있어야 한다.

```bash
source /opt/ros/jazzy/setup.bash
source ~/gui_camera_ws/install/local_setup.bash
ros2 topic echo /camera/eo --once
ros2 topic echo /camera/ir --once
```

여기서 이미지 메시지가 보이면 준비가 된 상태다.

---

## 5. 실행 방법

Jetson에서:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_camera_udp_bridge.sh
```

기본 workspace setup 경로는:

```text
~/gui_camera_ws/install/local_setup.bash
```

---

## 6. 환경변수 변경 예시

GUI IP를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
GUI_HOST=192.168.1.50 bash ./run_camera_udp_bridge.sh
```

workspace setup 경로를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
WORKSPACE_SETUP=/home/lig/other_ws/install/local_setup.bash bash ./run_camera_udp_bridge.sh
```

topic 이름을 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
EO_IMAGE_TOPIC=/some/eo/topic \
IR_IMAGE_TOPIC=/some/ir/topic \
bash ./run_camera_udp_bridge.sh
```

---

## 7. 정상 로그

정상이라면 실행 후 아래 로그가 보여야 한다.

```text
EO image topic: /camera/eo
IR image topic: /camera/ir
Streaming EO UDP packets to 192.168.1.94:5000
Streaming IR UDP packets to 192.168.1.94:5001
EO first frame sent!
IR first frame sent!
```

---

## 8. 요약

지금 방식은 가장 단순한 카메라 UDP 송출 방식이다.

- Docker 안 씀
- `minji` 이미지 안 씀
- YOLO 재추론 안 함
- `/camera/eo`, `/camera/ir`만 받아서 GUI로 UDP 송출
- EO는 `5000`
- IR는 `5001`

