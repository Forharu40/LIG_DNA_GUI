# JetsonThor.RosIrBridge

IR 카메라 영상이 ZYBO10 보드를 거쳐 Jetson의 ROS2 토픽으로 들어온 뒤, 운용통제 GUI로 넘어가도록 연결하는 브리지입니다.

현재 범위는 IR만 대상으로 합니다. EO는 기존 `5000` 포트를 유지하고, IR은 `5001` 포트로 분리해서 GUI에 전달합니다.

## 입력 ROS2 토픽

| 용도 | 기본 토픽 | 타입 |
| --- | --- | --- |
| IR YOLO 처리 영상 | `/yolo/ir/image_raw` | `sensor_msgs/msg/Image` |
| IR 전체 탐지 결과 | `/detections/ir` | `sentinel_interfaces/msg/Detection2DArray` |
| IR YOLO 상태 | `/yolo/ir/status` | `sentinel_interfaces/msg/YoloStatus` |

## GUI로 보내는 UDP 패킷

모든 패킷은 기본적으로 `GUI_HOST:5001`로 전송합니다.

| 종류 | 형식 | 설명 |
| --- | --- | --- |
| 영상 | `20바이트 헤더 + JPEG` | GUI의 IR 화면에 표시할 프레임 |
| 탐지 결과 | `DETS + JSON` | GUI가 직접 바운딩 박스와 라벨을 그림 |
| 상태 | `STAT + JSON` | YOLO 활성화, 모델 로드, threshold, 오류 상태 |

영상과 detection은 ROS2 `stamp`를 기준으로 같은 프레임을 맞추며, GUI 호환을 위해 같은 `frameId`도 함께 보냅니다.

## Jetson 실행

스크립트가 ROS2 배포판 setup과 워크스페이스 setup을 자동으로 찾고 source합니다. 기본적으로 `rclpy`와 `sentinel_interfaces` import까지 확인한 뒤 브리지를 실행합니다.

```bash
cd ~/LIG_DNA_GUI/JetsonThor.RosIrBridge
GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

워크스페이스 위치가 `/ros2_ws`가 아니면 다음처럼 지정합니다.

```bash
WORKSPACE_SETUP=~/ros2_ws/install/setup.bash GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

ROS2 배포판 setup 경로까지 직접 지정해야 하면 다음처럼 실행합니다.

```bash
ROS_SETUP=/opt/ros/humble/setup.bash WORKSPACE_SETUP=~/ros2_ws/install/setup.bash GUI_HOST=192.168.1.94 bash ./run_ir_gui_bridge.sh
```

만약 `ModuleNotFoundError: No module named 'rclpy'`가 다시 나오면, 먼저 아래를 확인하면 됩니다.

```bash
source /opt/ros/humble/setup.bash
source ~/ros2_ws/install/setup.bash
python3 -c "import rclpy; import sentinel_interfaces.msg; print('ok')"
```

## GUI 실행

운용통제 PC에서는 GUI를 실행하면 됩니다. GUI는 EO `5000`, IR `5001` 포트를 동시에 기다립니다.

```cmd
cd "C:\Users\buguen\Documents\New project"
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

## 확인 명령어

Jetson에서 IR 토픽이 실제로 나오는지 확인합니다.

```bash
ros2 topic hz /yolo/ir/image_raw
ros2 topic hz /detections/ir
ros2 topic echo /yolo/ir/status
```

브리지를 실행한 뒤 GUI 시스템 로그에 `IR UDP camera first frame received.`가 나오면 IR 영상 수신이 시작된 것입니다.
