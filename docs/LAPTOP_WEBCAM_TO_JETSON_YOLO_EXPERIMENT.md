# Laptop Webcam -> Jetson YOLO -> GUI Experiment

이 문서는 노트북 웹캠 영상을 Jetson으로 보내고, Jetson에서 기존 MEVA 데모가 사용하던 YOLO + GUI UDP 패킷 형식으로 처리하는 실험 구성을 정리합니다.

## 목표

실험 목표는 다음과 같습니다.

1. 노트북 웹캠 영상을 Jetson에 전달
2. Jetson에서 그 토픽을 입력으로 YOLO 수행
3. Jetson이 GUI가 이해하는 기존 `image + DETS + STAT` UDP 패킷을 송출
4. GUI에서 EO 실시간 영상과 바운딩 박스를 확인

## 이번 실험에서 사용한 토픽

문서 `YOLO_DETECTOR_GUI_INTERFACE (1).md`의 EO 입력 토픽 이름을 그대로 재사용했습니다.

- 입력: `/video/eo/preprocessed`
- 선택 재발행: `/yolo/eo/image_raw`

커스텀 `sentinel_interfaces` 메시지는 이번 실험에서 직접 만들지 않았습니다. 대신 GUI 바운딩 박스는 원래 GUI가 이미 이해하는 `DETS + JSON` UDP 패킷으로 전달합니다.

## 폴더 구성

### 1. 노트북 측

- [C:\Users\buguen\Documents\New project\LaptopWebcam.RosTopicPublisher](C:\Users\buguen\Documents\New%20project\LaptopWebcam.RosTopicPublisher)

역할:

- ROS2가 있을 때는 `/video/eo/preprocessed` 토픽 발행
- Windows CMD만 있을 때는 Jetson으로 UDP 송신

핵심 파일:

- [C:\Users\buguen\Documents\New project\LaptopWebcam.RosTopicPublisher\webcam_ros_topic_publisher.py](C:\Users\buguen\Documents\New%20project\LaptopWebcam.RosTopicPublisher\webcam_ros_topic_publisher.py)
- [C:\Users\buguen\Documents\New project\LaptopWebcam.RosTopicPublisher\webcam_udp_sender.py](C:\Users\buguen\Documents\New%20project\LaptopWebcam.RosTopicPublisher\webcam_udp_sender.py)
- [C:\Users\buguen\Documents\New project\LaptopWebcam.RosTopicPublisher\run_webcam_udp_sender.cmd](C:\Users\buguen\Documents\New%20project\LaptopWebcam.RosTopicPublisher\run_webcam_udp_sender.cmd)
- [C:\Users\buguen\Documents\New project\LaptopWebcam.RosTopicPublisher\README.md](C:\Users\buguen\Documents\New%20project\LaptopWebcam.RosTopicPublisher\README.md)

### 2. Jetson 측

- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker)

역할:

- ROS2 입력 모드:
  - `/video/eo/preprocessed` 구독
- Windows UDP 입력 모드:
  - 노트북이 보내는 UDP 웹캠 프레임 수신
- 공통:
  - Jetson에서 YOLO 수행
  - 선택적으로 `/yolo/eo/image_raw` 재발행
  - GUI UDP `5000`으로 EO 영상 + DETS + STAT 송출

핵심 파일:

- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker\Dockerfile](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker\Dockerfile)
- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker\run_webcam_topic_yolo.sh](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker\run_webcam_topic_yolo.sh)
- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker\run_webcam_udp_yolo.sh](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker\run_webcam_udp_yolo.sh)
- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker\app\webcam_topic_yolo_bridge.py](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker\app\webcam_topic_yolo_bridge.py)
- [C:\Users\buguen\Documents\New project\JetsonThor.WebcamTopicYoloDocker\app\webcam_udp_yolo_bridge.py](C:\Users\buguen\Documents\New%20project\JetsonThor.WebcamTopicYoloDocker\app\webcam_udp_yolo_bridge.py)

## 전체 흐름

```text
Windows laptop webcam
-> webcam_udp_sender.py
-> Jetson webcam_udp_yolo_bridge.py
-> YOLO inference on Jetson
-> GUI UDP 5000
-> BroadcastControl.App overlay

또는

Laptop webcam
-> /video/eo/preprocessed
-> Jetson webcam_topic_yolo_bridge.py
-> YOLO inference on Jetson
-> GUI UDP 5000
-> BroadcastControl.App overlay
```

## 실행 순서

### 1. GUI 실행

운용통제 PC에서 `BroadcastControl.App`를 실행합니다.

### 2. Jetson에서 YOLO 브리지 실행

Windows CMD용 권장 방식:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.WebcamTopicYoloDocker
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
LISTEN_PORT=5600 \
bash ./run_webcam_udp_yolo.sh --build
```

ROS2 토픽 방식:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.WebcamTopicYoloDocker
ROS_DOMAIN_ID=42 \
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
bash ./run_webcam_topic_yolo.sh --build
```

정상 로그 예시:

```text
Input image topic: /video/eo/preprocessed
Output image topic: /yolo/eo/image_raw
Streaming GUI packets to 192.168.1.94:5000
First webcam topic frame received.
First EO frame sent to GUI.
```

### 3. 노트북에서 웹캠 전송

Windows CMD 권장 방식:

```cmd
cd C:\work\LIG_DNA_GUI\LaptopWebcam.RosTopicPublisher
set JETSON_HOST=192.168.1.50
set JETSON_PORT=5600
run_webcam_udp_sender.cmd
```

기본값은 노트북에 별도 미리보기 창을 띄우지 않고, Jetson -> GUI 경로만 사용합니다.
즉 노트북은 송신만 하고, 실제 화면 확인은 `BroadcastControl.App` 안에서만 하게 됩니다.

ROS2 토픽 방식:

```bash
cd ~/LIG_DNA_GUI/LaptopWebcam.RosTopicPublisher
source /opt/ros/jazzy/setup.bash
export ROS_DOMAIN_ID=42
python3 ./webcam_ros_topic_publisher.py
```

## 왜 이렇게 바꿨는가

이번 실험은 아래 두 가지를 동시에 만족시키려는 목적입니다.

1. 노트북 웹캠 실시간 입력으로 Jetson YOLO를 시험
2. GUI는 기존 MEVA 데모와 같은 패킷 형식을 그대로 사용
3. Windows CMD 환경에서도 ROS2 설치 없이 빠르게 시도

즉 GUI를 별도로 바꾸지 않고도, 입력만 `MEVA video file`에서 `laptop webcam ROS topic`으로 바뀌도록 만든 것입니다.

## 제한 사항

- 현재 실험은 EO 1채널 기준입니다.
- `sentinel_interfaces` 커스텀 ROS 메시지는 직접 발행하지 않습니다.
- 바운딩 박스는 ROS detection topic이 아니라 `DETS + JSON` UDP 패킷으로 GUI에 전달됩니다.
- ROS2 토픽 방식에서는 노트북과 Jetson의 `ROS_DOMAIN_ID`가 반드시 같아야 합니다.
- Windows CMD UDP 방식에서는 ROS2가 없어도 됩니다.
