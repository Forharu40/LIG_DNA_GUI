# IR 카메라-ZYBO10-Jetson-GUI 연동 정리

이 문서는 IR 카메라 영상이 ZYBO10 보드를 거쳐 Jetson ROS2 시스템으로 들어온 뒤, 운용통제 GUI에 표시되는 흐름을 정리합니다.

현재 `ver4` 범위에서는 IR만 연동합니다. EO는 기존 구조를 유지합니다.

## 1. 전체 흐름

```text
IR 카메라
-> ZYBO10 보드
-> Jetson ROS2 영상/전처리/YOLO 노드
-> /yolo/ir/image_raw, /detections/ir, /yolo/ir/status
-> JetsonThor.RosIrBridge
-> UDP 5000
-> 운용통제 GUI IR 화면
```

## 2. GUI 포트 구성

| 영상 | GUI 수신 포트 | 설명 |
| --- | --- | --- |
| IR | `5000` | 현재 IR ROS2 브리지 경로 |
| EO | `5001` | ver4에서 예약한 EO 경로 |

GUI는 실행 시 두 포트를 동시에 엽니다. IR 브리지에서 첫 영상이 들어오면 시스템 로그에 `IR UDP camera first frame received.`가 표시됩니다.

## 3. Jetson IR 브리지 입력 토픽

| 용도 | 기본 토픽 | 타입 |
| --- | --- | --- |
| IR YOLO 처리 영상 | `/yolo/ir/image_raw` | `sensor_msgs/msg/Image` |
| IR 전체 탐지 결과 | `/detections/ir` | `sentinel_interfaces/msg/Detection2DArray` |
| IR YOLO 상태 | `/yolo/ir/status` | `sentinel_interfaces/msg/YoloStatus` |

이 토픽들은 사용자가 제공한 `YOLO_DETECTOR_GUI_INTERFACE.md` 명세를 기준으로 잡았습니다.

## 4. Jetson에서 GUI로 보내는 패킷

IR 브리지는 ROS2 토픽을 GUI가 이미 이해하는 UDP 패킷으로 바꿉니다.

| 패킷 | 형식 | 내용 |
| --- | --- | --- |
| 영상 | `20바이트 헤더 + JPEG` | IR 화면에 표시할 이미지 |
| 탐지 결과 | `DETS + JSON` | GUI가 직접 그릴 bbox 좌표와 라벨 |
| 상태 | `STAT + JSON` | YOLO 활성화, 모델 로드, threshold, 오류 |

영상은 GUI 송출용 해상도로 줄여 JPEG 인코딩하고, detection 좌표는 ROS2에서 받은 원본 픽셀 좌표를 유지합니다. GUI는 이를 화면 크기에 맞춰 바운딩 박스로 그립니다.

## 5. 실행 순서

### 5-1. 운용통제 PC

```cmd
cd "C:\Users\buguen\Documents\New project"
git fetch origin
git switch 2026_04_21_ver4/ir-zybo-jetson-gui-bridge
git pull
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

### 5-2. Jetson

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_21_ver4/ir-zybo-jetson-gui-bridge || git switch --track -c 2026_04_21_ver4/ir-zybo-jetson-gui-bridge origin/2026_04_21_ver4/ir-zybo-jetson-gui-bridge
git pull

cd ~/LIG_DNA_GUI/JetsonThor.RosIrBridge
GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

`GUI_HOST`에는 운용통제 PC의 현재 IP를 넣습니다.

워크스페이스 setup 경로가 다르면 다음처럼 바꿉니다.

```bash
WORKSPACE_SETUP=~/ros2_ws/install/setup.bash GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

ROS 배포판 setup까지 직접 지정해야 하면 다음처럼 실행합니다.

```bash
ROS_SETUP=/opt/ros/humble/setup.bash WORKSPACE_SETUP=~/ros2_ws/install/setup.bash GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

## 6. 확인 명령어

Jetson에서 먼저 ROS2 토픽이 살아 있는지 확인합니다.

```bash
ros2 topic list | grep -E "yolo|detection"
ros2 topic hz /yolo/ir/image_raw
ros2 topic hz /detections/ir
ros2 topic echo /yolo/ir/status
```

GUI 쪽 확인 기준은 다음과 같습니다.

- IR 작은 화면 또는 IR을 큰 화면으로 전환했을 때 영상이 보여야 합니다.
- IR을 큰 화면으로 전환하면 `/detections/ir` 기반 bbox가 표시됩니다.
- 시스템 로그에 `IR UDP stream receiver is waiting on port 5000.`가 보여야 합니다.
- 첫 프레임 수신 후 `IR UDP camera first frame received.`가 보여야 합니다.

만약 `ModuleNotFoundError: No module named 'rclpy'`가 나오면 Jetson에서 아래를 먼저 확인합니다.

```bash
source /opt/ros/humble/setup.bash
source ~/ros2_ws/install/setup.bash
python3 -c "import rclpy; import sentinel_interfaces.msg; print('ok')"
```

## 7. 조절 가능한 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `GUI_HOST` | `192.168.1.94` | 운용통제 PC IP |
| `GUI_PORT` | `5000` | GUI IR UDP 수신 포트 |
| `IMAGE_TOPIC` | `/yolo/ir/image_raw` | IR 영상 토픽 |
| `DETECTION_TOPIC` | `/detections/ir` | IR detection 토픽 |
| `STATUS_TOPIC` | `/yolo/ir/status` | IR YOLO 상태 토픽 |
| `JPEG_QUALITY` | `45` | GUI 전송용 JPEG 품질 |
| `STREAM_MAX_WIDTH` | `854` | GUI 전송용 최대 가로 해상도 |
| `STREAM_MAX_HEIGHT` | `480` | GUI 전송용 최대 세로 해상도 |
| `MAX_UDP_BYTES` | `55000` | UDP 한 패킷 최대 목표 크기 |

끊김이 심하면 `STREAM_MAX_WIDTH`, `STREAM_MAX_HEIGHT`, `JPEG_QUALITY`를 낮추고, 화질이 부족하면 조금씩 올려 테스트합니다.
