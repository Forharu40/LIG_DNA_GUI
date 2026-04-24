# JetsonThor.MevaVideoDemoDocker

MEVA 영상 파일을 이용해서 GUI 영상 송출과 YOLO 결과 표시를 테스트하는 전용 폴더다.

이 폴더는 나중에 실제 카메라 테스트가 끝나면 통째로 지워도 된다.  
카메라 ROS2 브리지와는 분리되어 있다.

## 포함 내용

- `Dockerfile`
- `run_meva_yolo_demo.sh`
- `app/stream_meva_yolo.py`

## 용도

- MEVA 영상 파일을 읽어서
- YOLO를 수행하고
- GUI로 UDP 영상과 탐지 결과를 보내는 데모/테스트용

## 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaVideoDemoDocker
bash ./run_meva_yolo_demo.sh --build
```
