# LaptopWebcam.RosTopicPublisher

노트북 웹캠 영상을 ROS2 이미지 토픽으로 발행해서 Jetson으로 보내는 실험용 폴더입니다.

이 실험은 `YOLO_DETECTOR_GUI_INTERFACE (1).md`에 나온 EO 입력 토픽 이름을 그대로 재사용합니다.

- 입력 토픽: `/video/eo/preprocessed`
- 메시지 타입: `sensor_msgs/msg/Image`

Jetson 쪽 YOLO 브리지는 이 토픽을 구독해서

- YOLO 추론
- `/yolo/eo/image_raw` 재발행
- GUI UDP `5000` 포트로 실시간 EO 영상 + DETS + STAT 송출

을 수행합니다.

## 준비 조건

- 노트북에 ROS2 Jazzy 이상이 설치되어 있어야 합니다.
- `sensor_msgs`를 사용할 수 있어야 합니다.
- `python3-opencv` 또는 `opencv-python`이 설치되어 있어야 합니다.
- 노트북과 Jetson이 같은 네트워크에 있어야 합니다.
- `ROS_DOMAIN_ID`를 노트북과 Jetson에서 동일하게 맞춰야 합니다.

## 실행 예시

Linux/macOS:

```bash
cd ~/LIG_DNA_GUI/LaptopWebcam.RosTopicPublisher
source /opt/ros/jazzy/setup.bash
export ROS_DOMAIN_ID=42
python3 ./webcam_ros_topic_publisher.py
```

Windows PowerShell:

```powershell
cd C:\work\LIG_DNA_GUI\LaptopWebcam.RosTopicPublisher
call C:\dev\ros2_jazzy\local_setup.bat
$env:ROS_DOMAIN_ID="42"
python .\webcam_ros_topic_publisher.py
```

## 주요 환경 변수

| 이름 | 기본값 | 설명 |
|---|---|---|
| `WEBCAM_TOPIC` | `/video/eo/preprocessed` | Jetson으로 보낼 ROS2 이미지 토픽 |
| `CAMERA_INDEX` | `0` | 노트북 웹캠 인덱스 |
| `FRAME_WIDTH` | `1280` | 발행 영상 폭 |
| `FRAME_HEIGHT` | `720` | 발행 영상 높이 |
| `TARGET_FPS` | `10` | 발행 FPS |
| `FRAME_ID` | `laptop_webcam_eo` | ROS 이미지 `header.frame_id` |
| `SHOW_PREVIEW` | `true` | 노트북에서 미리보기 창 표시 여부 |

## 흐름

```text
Laptop webcam
-> /video/eo/preprocessed
-> Jetson YOLO bridge
-> GUI UDP 5000
```
