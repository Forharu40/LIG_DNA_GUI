# GUI_camera YOLO Node Guide

이 문서는 기존 EO/IR 카메라 및 MEVA 영상 송출 구조를 정리하고, 새로 추가한 `GUI_camera` 컨테이너와 `gui_camera_yolo_node.py`가 무엇을 바꾸는지 설명한다.

## 1. 기존 구조 정리

### 1-1. 카메라에서 Jetson ROS2까지

기존 YOLO 인터페이스 문서 기준으로 카메라 쪽 입력 흐름은 아래와 같다.

- EO 입력 영상 토픽: `/video/eo/preprocessed`
- EO 입력 프레임 정보: `/video/eo/preprocessed/frame_info`
- IR 입력 영상 토픽: `/video/ir/preprocessed`
- IR 입력 프레임 정보: `/video/ir/preprocessed/frame_info`

즉, 카메라 원본이 바로 YOLO로 들어가는 것이 아니라, 전처리 노드를 거친 뒤 `preprocessed` 토픽으로 YOLO 노드가 영상을 받는 구조다.

### 1-2. Jetson ROS2에서 GUI가 주로 보는 토픽

YOLO 노드가 실제로 만들어내는 핵심 출력은 아래와 같다.

EO:

- `/yolo/eo/image_raw`
- `/detections/eo`
- `/driver/eo/detection`
- `/yolo/eo/status`

IR:

- `/yolo/ir/image_raw`
- `/detections/ir`
- `/driver/ir/detection`
- `/yolo/ir/status`

의미는 다음과 같다.

- `/yolo/*/image_raw`
  - YOLO와 동기화된 실제 영상 프레임
- `/detections/*`
  - 한 프레임 안에 있는 전체 bbox 배열
- `/driver/*/detection`
  - 대표 객체 1개의 중심 좌표
- `/yolo/*/status`
  - 모델 로드 여부, 활성화 여부, threshold, 최근 에러

## 2. Jetson에서 GUI로 보내는 패킷

기존 저장소에서 GUI 송출 형식은 크게 3종류다.

### 2-1. 영상 패킷

MEVA 송출기와 live camera sender, IR ROS bridge가 공통으로 쓰는 형식이다.

구조:

```text
20-byte header + JPEG bytes
```

헤더 형식:

```text
!QIIHH
```

헤더 필드:

- `stampNs`
- `frameId`
- `jpegLength`
- `width`
- `height`

즉 Jetson은 GUI에 JPEG 프레임 하나를 UDP로 보내고, GUI는 이 패킷을 디코드해 EO 또는 IR 화면에 표시한다.

### 2-2. Detection 패킷

구조:

```text
DETS + compact JSON
```

주요 JSON 필드:

- `stampNs`
- `frameId`
- `width`
- `height`
- `detections[]`

GUI는 이 패킷을 받아 bbox overlay를 직접 그린다.

### 2-3. Status 패킷

구조:

```text
STAT + compact JSON
```

주요 JSON 필드:

- `enabled`
- `modelLoaded`
- `confThreshold`
- `lastError`
- `source`
- `stampNs`
- `frameId`

GUI는 이 상태 패킷을 시스템 로그, 에러 표시, 모델 상태 반영에 사용한다.

## 3. 기존 송출 방식의 차이

### 3-1. MEVA 데모 송출

기존 `stream_meva_yolo.py`는 파일 기반 데모 경로다.

흐름:

```text
MEVA video file
-> OpenCV read
-> YOLO detect
-> JPEG encode
-> UDP image + detection + status
-> GUI
```

장점:

- GUI 패킷 형식 검증에 좋음
- 카메라가 없어도 EO 화면 동작 확인 가능

한계:

- 실제 카메라 경로와는 다름
- 파일 샘플링, 타임라인, 데모 재생 논리가 들어감

### 3-2. IR ROS bridge

기존 `ros_ir_to_gui_bridge.py`는 ROS2 토픽을 받아 GUI 패킷으로 보내는 브리지다.

흐름:

```text
IR ROS2 topics
-> ros_ir_to_gui_bridge.py
-> UDP image + detection + status
-> GUI IR port
```

장점:

- ROS2 토픽 구조와 GUI를 직접 연결함
- 실제 시스템의 토픽과 더 잘 맞음

## 4. 이번에 추가한 GUI_camera 구조

이번 작업에서는 `minji-perception` 이미지를 기반으로, MEVA sender가 사용하던 YOLO 런타임 환경과 JPEG/UDP 최적화 로직을 함께 흡수한 새 이미지를 만들고, IR ROS bridge의 ROS2 연동 방식을 결합한 새 노드를 추가했다.

추가 파일:

- `JetsonThor.MevaYoloDocker/app/gui_camera_yolo_node.py`
- `JetsonThor.MevaYoloDocker/Dockerfile.gui_camera`
- `JetsonThor.MevaYoloDocker/run_gui_camera_yolo_node.sh`

컨테이너 이미지 이름:

- 사용자 개념 이름: `GUI_camera`
- 실제 Docker tag: `gui_camera`

이 이미지는 개념적으로 아래 두 이미지를 합친 형태다.

- ROS2/기존 시스템 기반: `minji-perception`
- YOLO sender 기준 이미지: `ultralytics/ultralytics:latest-nvidia-arm64`

## 5. 새 노드가 하는 일

`gui_camera_yolo_node.py`는 하나의 ROS2 노드다.

기본 동작:

1. `/video/eo/preprocessed` 를 subscribe
2. 입력 프레임을 `/yolo/eo/image_raw` 로 republish
3. 같은 프레임으로 YOLO 추론 수행
4. 결과를 `/detections/eo` 로 publish
5. 대표 객체 중심을 `/driver/eo/detection` 로 publish
6. 상태를 `/yolo/eo/status` 로 publish
7. 동시에 GUI용 UDP 패킷도 전송

즉, 한 노드가 아래 두 역할을 함께 수행한다.

- ROS2 YOLO output node
- GUI UDP sender

흐름:

```text
/video/eo/preprocessed
-> gui_camera_yolo_node.py
   -> /yolo/eo/image_raw
   -> /detections/eo
   -> /driver/eo/detection
   -> /yolo/eo/status
   -> UDP image packet
   -> UDP DETS packet
   -> UDP STAT packet
-> GUI EO screen
```

## 6. 무엇을 최적화했는가

이 노드는 단순히 minji 이미지만 쓰는 것이 아니라, 기존 MEVA/live sender에서 이미 검증된 GUI 송출 로직과 YOLO 런타임 준비 방식을 함께 가져와 ROS2 카메라 토픽용으로 바꿨다.

핵심 최적화 포인트:

1. ROS2 topic input 사용
   - 파일 재생이 아니라 실제 카메라 토픽을 직접 사용

2. JPEG/UDP GUI packet format 재사용
   - 기존 GUI 파서를 그대로 쓸 수 있음

3. 비동기 worker 분리
   - YOLO 추론 worker
   - JPEG encode worker
   - UDP send worker

4. 최신 프레임 중심 구조
   - 긴 큐를 쌓기보다 최신 프레임 위주로 처리

5. tile inference 옵션 유지
   - 필요하면 작은 객체 검출용으로 확장 가능

6. ROS2 publish와 GUI forward 동시 지원
   - ROS2 토픽도 유지
   - GUI도 바로 송출 가능

## 7. 기본 실행 전제

이 구조는 아래를 가정한다.

- Jetson 안에 `minji-perception` Docker image가 존재
- ROS2 workspace가 예를 들어 `/home/lig/minji/ros2_ws` 에 존재
- workspace 안에 `sentinel_interfaces` 와 관련 패키지가 빌드되어 있음
- 카메라 전처리 토픽 `/video/eo/preprocessed` 가 실제로 올라오고 있음

## 8. 빌드 및 실행 방법

Jetson에서:

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_23_ver2_gui-camera-node
git pull
```

그 다음 실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_gui_camera_yolo_node.sh --build
```

기본값:

- image name: `gui_camera`
- ROS base image: `minji-perception`
- YOLO reference image: `ultralytics/ultralytics:latest-nvidia-arm64`
- bundled model: `yolo11s.pt`
- workspace: `$HOME/minji/ros2_ws`
- input topic: `/video/eo/preprocessed`
- GUI target: `192.168.1.94:5000`

### 8-1. 명시적으로 실행하는 예시

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
IMAGE_NAME=GUI_camera \
ROS_BASE_IMAGE=minji-perception \
YOLO_REFERENCE_IMAGE=ultralytics/ultralytics:latest-nvidia-arm64 \
MODEL_NAME=yolo11s.pt \
WORKSPACE_DIR=/home/lig/minji/ros2_ws \
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
INPUT_IMAGE_TOPIC=/video/eo/preprocessed \
SYNCED_IMAGE_TOPIC=/yolo/eo/image_raw \
DETECTION_TOPIC=/detections/eo \
DRIVER_DETECTION_TOPIC=/driver/eo/detection \
STATUS_TOPIC=/yolo/eo/status \
MODEL_PATH=/ros2_ws/src/yolo_detector_pkg/model/best.onnx \
CONFIDENCE=0.60 \
DETECTION_INTERVAL_SECONDS=0.20 \
bash ./run_gui_camera_yolo_node.sh --build
```

## 9. 어떻게 구동되는가

실행 스크립트 `run_gui_camera_yolo_node.sh`는 아래를 수행한다.

1. `GUI_camera` 이미지를 `Dockerfile.gui_camera`로 빌드
2. `minji-perception` 을 ROS base image로 사용
3. MEVA sender 계열 이미지가 쓰던 Ultralytics 기반 환경에서 모델을 준비
4. 최종 이미지에 YOLO 모델과 의존성을 포함
5. Jetson ROS2 workspace를 `/ros2_ws` 로 mount
6. 컨테이너 안에서:
   - `source /opt/ros/jazzy/setup.bash`
   - `source /ros2_ws/install/setup.bash`
   - `python3 /app/gui_camera_yolo_node.py`
7. 노드가 ROS2 input topic을 받아 YOLO를 수행
8. ROS2 output topic publish + GUI UDP packet 송출

## 10. 기존 방식과 비교

### 기존 EO live sender

- OpenCV device 기반
- 카메라 장치 `/dev/videoX` 직접 열기
- GUI 송출에는 좋지만 ROS2 토픽 체계와는 분리됨

### 새 GUI_camera node

- ROS2 topic 기반
- 기존 YOLO 인터페이스 문서와 잘 맞음
- GUI 송출도 가능
- YOLO 결과를 ROS2와 GUI에 동시에 제공

즉, 새 구조는 EO 실카메라 경로를 ROS2와 GUI 양쪽에 동시에 맞추는 방향이다.

## 11. 확인 포인트

Jetson에서:

```bash
ros2 topic hz /yolo/eo/image_raw
ros2 topic hz /detections/eo
ros2 topic echo /yolo/eo/status --once
```

GUI에서는:

- EO receiver가 `5000` 포트에서 대기하는지 확인
- 첫 프레임 수신 로그 확인
- detection overlay가 정상적으로 그려지는지 확인

## 12. 요약

이번 변경은 아래를 목표로 했다.

- EO/IR 문서 기준의 ROS2 토픽 구조 유지
- MEVA sender에서 검증된 JPEG/UDP GUI packet 형식 재사용
- `minji-perception` 기반의 새 이미지 `GUI_camera` 추가
- ROS2 카메라 토픽을 받아 YOLO 추론과 GUI 송출을 동시에 수행하는 새 노드 추가

한 문장으로 정리하면:

```text
카메라 토픽 -> GUI_camera YOLO node -> ROS2 detection/status/image + GUI UDP packet 동시 출력
```
