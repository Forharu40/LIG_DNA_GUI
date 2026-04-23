# GUI_camera Dual Bridge Guide

이 문서는 현재 장비 구조에 맞춰 `gui_camera`를 **EO/IR ROS2 이미지 토픽 -> GUI UDP 영상 송출 브리지**로 사용하는 방법을 정리한 문서다.

지금 `gui_camera`는 Jetson에서 카메라 장치를 직접 여는 컨테이너가 아니다.  
기존 시스템이 이미 만들고 있는 ROS2 이미지 토픽을 받아서, GUI가 바로 표시할 수 있는 UDP JPEG 패킷으로 바꿔 보내는 역할만 담당한다.

---

## 1. 쉽게 보는 node / topic / packet

### Node

`node`는 하나의 실행 프로그램이다.

예를 들면:

- 카메라 영상을 수신하는 프로그램
- YOLO 추론을 수행하는 프로그램
- GUI로 영상을 보내는 프로그램

이번 `gui_camera`는 **GUI 송출 전용 bridge node**다.

### Topic

`topic`은 ROS2 안에서 node끼리 데이터를 주고받는 통신선이다.

이번 구조에서 `gui_camera`가 구독하는 기본 topic은 다음과 같다.

- EO: `/camera/eo`
- IR: `/camera/ir`

즉, 기존 카메라/수신 시스템이 이 토픽으로 이미지를 발행하고, `gui_camera`는 그 이미지를 받아서 GUI로 전달한다.

### Packet

`packet`은 Jetson과 GUI PC 사이를 실제로 오가는 UDP 데이터다.

이번 `gui_camera`는 topic을 받아 아래 형태의 이미지 packet으로 바꿔 보낸다.

- EO -> UDP `5000`
- IR -> UDP `5001`

정리하면:

- `node` = 실행 프로그램
- `topic` = ROS2 내부 통신
- `packet` = GUI로 가는 실제 UDP 데이터

---

## 2. 현재 구조

현재 장비 구조는 아래처럼 이해하면 된다.

```text
EO/IR camera + ZYBO10 + 기존 수신 시스템
-> ROS2 image topics (/camera/eo, /camera/ir)
-> gui_camera bridge container
-> GUI UDP packets
-> BroadcastControl GUI
```

즉 `gui_camera`는:

- 카메라를 직접 열지 않음
- YOLO를 새로 돌리지 않음
- EO/IR 이미지 topic만 받아서 GUI로 보냄

---

## 3. 기본 topic / port 매핑

기본값은 아래와 같다.

| 종류 | ROS2 topic | GUI UDP port |
|---|---|---|
| EO | `/camera/eo` | `5000` |
| IR | `/camera/ir` | `5001` |

기본 GUI host는:

- `192.168.1.94`

필요하면 실행할 때 환경변수로 바꿀 수 있다.

---

## 4. GUI로 보내는 packet 구조

영상 packet은 아래 구조다.

```text
20-byte header + JPEG bytes
```

헤더 포맷:

```text
!QIIHH
```

의미:

- `Q`: `stampNs` - 프레임 타임스탬프(ns)
- `I`: `frameId` - 브리지 내부 프레임 번호
- `I`: `jpegLength` - JPEG 바이트 길이
- `H`: `width` - 전송 이미지 width
- `H`: `height` - 전송 이미지 height

현재 기본 전송 크기:

- `640 x 360`

현재 기본 JPEG 품질:

- `35`

---

## 5. 포함 파일

- `JetsonThor.MevaYoloDocker/Dockerfile.gui_camera`
- `JetsonThor.MevaYoloDocker/app/gui_camera_yolo_node.py`
- `JetsonThor.MevaYoloDocker/run_gui_camera_yolo_node.sh`

설명:

- `gui_camera_yolo_node.py`
  - EO/IR ROS2 이미지를 받아 UDP JPEG packet으로 바꾸는 bridge
- `Dockerfile.gui_camera`
  - bridge 컨테이너 이미지 정의
- `run_gui_camera_yolo_node.sh`
  - bridge 컨테이너 실행 스크립트

---

## 6. 실행 방법

Jetson에서 브랜치 업데이트:

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_23_ver3_gui-camera-bridge
git pull
```

그 다음 bridge 실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_gui_camera_yolo_node.sh --build
```

---

## 7. 실행 시 기본 동작

스크립트를 그냥 실행하면 자동으로:

- EO topic `/camera/eo` 구독
- IR topic `/camera/ir` 구독
- EO를 GUI `5000` 포트로 송출
- IR를 GUI `5001` 포트로 송출

즉 별도 옵션 없이도 아래 기준으로 동작한다.

```text
/camera/eo -> 192.168.1.94:5000
/camera/ir -> 192.168.1.94:5001
```

---

## 8. 실행 로그에서 정상 확인 포인트

정상이라면 컨테이너 로그에 아래 메시지가 보여야 한다.

```text
EO image topic: /camera/eo
IR image topic: /camera/ir
Streaming EO GUI packets to 192.168.1.94:5000
Streaming IR GUI packets to 192.168.1.94:5001
First EO frame forwarded to GUI.
First IR frame forwarded to GUI.
```

특히 마지막 두 줄이 중요하다.

- `First EO frame forwarded to GUI.`
- `First IR frame forwarded to GUI.`

이 로그가 나오면 실제 프레임이 GUI로 나가기 시작한 상태다.

---

## 9. 실행 전 확인할 것

기존 ROS2 시스템이 먼저 `/camera/eo`, `/camera/ir`를 발행하고 있어야 한다.

Jetson에서 확인:

```bash
source /opt/ros/jazzy/setup.bash
source /home/lig/minji/ros2_ws/install/setup.bash
ros2 topic list | grep -E "^/camera/eo$|^/camera/ir$"
ros2 topic echo /camera/eo --once
ros2 topic echo /camera/ir --once
```

여기서 메시지가 실제로 보여야 `gui_camera`도 영상을 전달할 수 있다.

---

## 10. 환경변수로 바꾸는 방법

예를 들어 GUI PC IP를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
GUI_HOST=192.168.1.50 bash ./run_gui_camera_yolo_node.sh --build
```

예를 들어 topic 이름이 다르면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
EO_IMAGE_TOPIC=/yolo/eo/image_raw \
IR_IMAGE_TOPIC=/yolo/ir/image_raw \
bash ./run_gui_camera_yolo_node.sh --build
```

예를 들어 해상도를 바꾸고 싶으면:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
STREAM_WIDTH=854 \
STREAM_HEIGHT=480 \
bash ./run_gui_camera_yolo_node.sh --build
```

---

## 11. 요약

이번 브랜치의 `gui_camera`는 현재 장비 구조에 맞게 아래 역할만 한다.

```text
ROS2 image topics (/camera/eo, /camera/ir)
-> gui_camera bridge
-> GUI UDP packets
-> EO 5000 / IR 5001
```

즉:

- EO는 `5000`
- IR는 `5001`
- 카메라는 직접 열지 않음
- YOLO를 재추론하지 않음
- 기존 ROS2 이미지 topic을 GUI용 UDP 영상으로 바꿔주는 bridge 전용 컨테이너

