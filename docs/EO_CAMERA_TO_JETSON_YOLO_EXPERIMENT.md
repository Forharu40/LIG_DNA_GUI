# EO Camera -> Jetson YOLO -> GUI Experiment

이 브랜치는 웹캠 경로를 제외하고, 기존 EO 카메라 토픽을 Jetson에서 받아 YOLO를 수행한 뒤 GUI에 바운딩 박스를 포함해 표시하는 실험용 구성입니다.

목표:

1. EO 카메라 입력 토픽 수신
2. Jetson에서 YOLO 추론
3. GUI로 `image + DETS + STAT` UDP 패킷 전송
4. BroadcastControl.App EO 화면에서 영상 + 바운딩 박스 표시

입력 토픽:
- `/video/eo/preprocessed`

출력:
- GUI `5000`
- optional `/yolo/eo/image_raw`

사용 폴더:
- [C:\Users\buguen\Documents\New project\JetsonThor.EoTopicYoloDocker](C:\Users\buguen\Documents\New%20project\JetsonThor.EoTopicYoloDocker)

실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.EoTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
YOLO_DEVICE=0 \
YOLO_HALF=true \
METRICS_LOG_INTERVAL=1.0 \
bash ./run_eo_topic_yolo.sh --build
```

정상이라면 Jetson 터미널 또는 `docker logs`에서 아래와 같은 로그를 봅니다.

```text
Input image topic: /video/eo/preprocessed
Streaming GUI packets to 192.168.1.94:5000
YOLO device: 0 (cuda_available=True)
YOLO half precision: True
First EO camera topic frame received.
First EO frame sent to GUI.
[metrics] input_fps=14.2 gui_fps=13.8 infer_ms=28.4 frame=1280x720 gpu_alloc_mb=812.4 gpu_reserved_mb=1024.0
```
