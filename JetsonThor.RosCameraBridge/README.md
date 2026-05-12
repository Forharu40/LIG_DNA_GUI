# JetsonThor.RosCameraBridge

Jetson에서 발행되는 ROS2 EO/IR 영상 토픽과 YOLO detection 토픽을 구독한 뒤,
운용통제 GUI가 수신할 수 있는 UDP 패킷으로 변환해 전송하는 브릿지입니다.

## 포함 파일

- `Dockerfile`
- `run_camera_udp_bridge.sh`
- `app/camera_udp_bridge.py`

## 기본 입력 토픽

- EO image: `/video/eo/preprocessed`
- IR image: `/camera/ir`
- EO detection: `/detections/eo`
- IR detection: `/detections/ir`

## 기본 GUI UDP 포트

- EO GUI 수신 포트: `6000`
- IR GUI 수신 포트: `6001`

IR 카메라가 Zybo에서 Jetson `video_rx_node`로 들어올 때 `5001` 포트를 사용하므로,
Jetson 브릿지에서 PC GUI로 보내는 포트는 혼동을 피하기 위해 `6000/6001`을 사용합니다.

## 데이터 흐름

```text
Jetson ROS2 /video/eo/preprocessed -> gui_camera_bridge -> GUI UDP 6000
Jetson ROS2 /camera/ir             -> gui_camera_bridge -> GUI UDP 6001
Jetson ROS2 /detections/eo         -> gui_camera_bridge -> GUI UDP 6000
Jetson ROS2 /detections/ir         -> gui_camera_bridge -> GUI UDP 6001
```

영상은 JPEG 패킷으로, detection은 JSON 기반 `DETS` 패킷으로 GUI에 전송됩니다.

## 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.RosCameraBridge

GUI_HOST=192.168.1.94 \
JETSON_RECORDING_DIR=/home/lig/Desktop/video \
RECORDING_SEGMENT_SECONDS=60 \
RECORDING_HTTP_PORT=8090 \
bash ./run_camera_udp_bridge.sh --build
```
