# JetsonThor.CameraYoloDocker

Jetson이 카메라 장치를 직접 열 수 있을 때, 라이브 카메라 영상에 YOLO를 수행해서 GUI로 보내는 실험용 폴더다.

이 폴더는 ROS2 topic 브리지와는 별개다.

## 포함 내용

- `Dockerfile`
- `run_live_camera_yolo.sh`
- `app/stream_live_camera_yolo.py`

## 용도

- `/dev/video0` 같은 카메라 장치를 직접 열고
- YOLO를 수행한 뒤
- GUI로 UDP 영상과 탐지 결과를 보내는 실험용

## 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.CameraYoloDocker
bash ./run_live_camera_yolo.sh --build
```
