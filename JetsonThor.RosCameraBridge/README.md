# JetsonThor.RosCameraBridge

ROS2 토픽을 받아서 GUI로 넘기는 EO/IR 통합 브리지 폴더다.

이 폴더는 기존 `JetsonThor.RosIrBridge`의 IR 전용 구조를 대신해서, EO와 IR를 한 번에 다룬다.

## 포함 내용

- `Dockerfile`
- `run_camera_udp_bridge.sh`
- `app/camera_udp_bridge.py`

## 기본 입력 topic

- EO: `/camera/eo`
- IR: `/camera/ir`

## 기본 GUI UDP port

- EO: `5000`
- IR: `5001`

## 용도

- ROS2 이미지 토픽을 구독
- 640x360 JPEG로 압축
- GUI로 UDP 전송

즉 구조는 아래와 같다.

```text
ROS2 /camera/eo -> GUI UDP 5000
ROS2 /camera/ir -> GUI UDP 5001
```

## 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.RosCameraBridge
bash ./run_camera_udp_bridge.sh --build
```
