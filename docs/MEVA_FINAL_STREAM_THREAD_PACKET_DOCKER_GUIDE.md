# MEVA 최종 송출 구조 정리

이 문서는 `2026_04_21_ver2/stream-thread-pipeline` 브랜치 기준으로, Jetson Thor Docker 프로그램이 MEVA 영상을 GUI로 보내는 구조를 정리한 문서입니다.

## 1. 전체 역할

현재 시스템은 Jetson Thor에서 MEVA 데모 영상을 읽고, YOLO 탐지 결과와 함께 운용통제 GUI로 UDP 전송합니다.

```text
Jetson Docker
-> MEVA 영상 읽기
-> GUI 송출용 저해상도 JPEG 인코딩
-> YOLO 탐지 결과 생성
-> UDP 패킷 전송
-> 운용통제 GUI EO 화면 표시
```

GUI는 Jetson에서 받은 JPEG 영상은 EO 화면에 표시하고, detection JSON은 GUI에서 직접 바운딩 박스와 라벨로 그립니다.

## 2. 쓰레드 분리 구조

현재 Jetson 프로그램은 작업을 최대한 나누기 위해 4개의 흐름으로 구성되어 있습니다.

| 구분 | 담당 작업 | 코드 위치 |
| --- | --- | --- |
| 메인 루프 | MEVA 프레임 읽기, 재생 구간 관리, 프레임 타이밍 유지 | `main()` |
| YOLO 쓰레드 | 원본 또는 고해상도 프레임으로 객체 탐지 | `ThreadPoolExecutor(max_workers=1)` |
| JPEG 인코딩 쓰레드 | GUI 송출용 프레임을 JPEG로 압축 | `ENABLE_ASYNC_ENCODING=true` |
| UDP 전송 쓰레드 | 영상 패킷과 detection 패킷을 GUI로 전송 | `ENABLE_ASYNC_UDP_SEND=true` |

### 2-1. 메인 루프

메인 루프는 영상 재생의 중심입니다.

주요 작업은 다음과 같습니다.

```text
1. MEVA 파일에서 프레임 읽기
2. GUI 송출용 프레임 크기로 축소
3. YOLO 작업이 필요하면 YOLO 쓰레드에 프레임 복사본 전달
4. JPEG 인코딩 작업이 비어 있으면 인코딩 쓰레드에 프레임 전달
5. 전송 작업이 끝났으면 다음 프레임 전송 준비
6. 재생 타이밍 유지 및 필요 시 프레임 스킵
```

메인 루프는 YOLO, JPEG 인코딩, UDP 전송을 직접 기다리지 않도록 설계되어 있습니다. 그래서 무거운 작업이 잠깐 느려져도 영상 재생 흐름이 같이 멈추는 현상을 줄입니다.

### 2-2. YOLO 쓰레드

YOLO 쓰레드는 객체 탐지를 담당합니다.

```python
detection_executor = ThreadPoolExecutor(max_workers=1)
```

탐지는 기본적으로 `0.5초`마다 한 번 수행합니다.

```text
DETECTION_INTERVAL_SECONDS=0.5
```

중요한 점은 YOLO가 GUI 송출용 저화질 프레임이 아니라 원본 또는 별도 추론용 프레임을 기준으로 탐지한다는 점입니다. 따라서 GUI 영상은 낮은 화질로 부드럽게 보내면서, 탐지 좌표는 더 좋은 프레임 기준으로 만들 수 있습니다.

### 2-3. JPEG 인코딩 쓰레드

JPEG 인코딩 쓰레드는 GUI에 보낼 영상 프레임을 JPEG로 압축합니다.

```text
ENABLE_ASYNC_ENCODING=true
```

이 옵션이 켜져 있으면 메인 루프가 직접 `cv2.imencode()`를 수행하지 않고, 인코딩 작업자에게 맡깁니다.

효과는 다음과 같습니다.

```text
기존:
프레임 읽기 -> JPEG 인코딩 끝날 때까지 대기 -> UDP 전송

현재:
프레임 읽기 -> 인코딩 쓰레드에 전달 -> 메인 루프는 다음 타이밍 처리
```

### 2-4. UDP 전송 쓰레드

UDP 전송 쓰레드는 인코딩된 JPEG 영상 패킷과 detection 패킷을 GUI로 보냅니다.

```text
ENABLE_ASYNC_UDP_SEND=true
```

화질을 올리면 JPEG 크기가 커지고 `sendto()` 호출이 순간적으로 늦어질 수 있습니다. UDP 전송 쓰레드는 이 전송 지연이 메인 루프와 인코딩 루프를 직접 막지 않도록 분리합니다.

UDP 송신 버퍼는 기본값으로 `1048576 bytes`를 사용합니다.

```text
UDP_SEND_BUFFER_BYTES=4194304
```

더 높은 화질 테스트 시에는 다음처럼 늘릴 수 있습니다.

```bash
UDP_SEND_BUFFER_BYTES=4194304 bash ./run_meva_yolo_demo.sh
```

## 3. 토픽 사용 여부

현재 버전은 ROS2 토픽을 사용하지 않습니다.

초기 명세에서는 `/yolo/image_raw`, `/detections`, `/yolo/status`처럼 토픽을 분리하는 구조를 고려했지만, 현재 구현은 운용통제 GUI와 직접 UDP로 연동하는 데모 구조입니다.

따라서 현재 구조는 다음과 같습니다.

```text
ROS2 topic 사용 안 함
Jetson Docker -> UDP 5000 -> GUI
```

논리적으로는 다음처럼 토픽 역할을 UDP 패킷 종류로 나누고 있습니다.

| 논리 역할 | ROS2식 이름으로 보면 | 현재 구현 |
| --- | --- | --- |
| 영상 | `/yolo/image_raw` | JPEG image packet |
| 탐지 결과 | `/detections` | `DETS + JSON` packet |
| 상태 | `/yolo/status` | `STAT + JSON` packet |
| 구간 정보 | 별도 메타데이터 | `MEVA` metadata packet |

## 4. UDP 패킷 구조

모든 패킷은 기본적으로 GUI의 UDP `5000` 포트로 전송됩니다.

```text
Jetson Docker -> GUI_HOST:GUI_PORT
기본 GUI_PORT=5000
```

### 4-1. MEVA 구간 메타데이터 패킷

MEVA 영상이 어느 구간을 재생 중인지 알려주는 패킷입니다.

```python
struct.pack(">4sIHHIIIIII", ...)
```

첫 4바이트는 다음 값입니다.

```text
MEVA
```

포함 정보는 다음과 같습니다.

| 값 | 의미 |
| --- | --- |
| magic | `MEVA` |
| version/reserved | 현재 0 |
| clip_index | 현재 재생 중인 샘플 클립 번호 |
| clip_count | 전체 샘플 클립 수 |
| segment_start_seconds | 재생 구간 시작 시간 |
| segment_end_seconds | 재생 구간 종료 시간 |
| current_playback_seconds | 현재 재생 시점 |
| cycle_index | 반복 재생 회차 |

GUI는 이 패킷을 받아 시스템 로그에 현재 MEVA 구간 정보를 표시할 수 있습니다.

### 4-2. 영상 JPEG 패킷

영상 패킷은 다음 구조입니다.

```text
20바이트 헤더 + JPEG 데이터
```

헤더 구조는 다음과 같습니다.

```python
struct.pack("!QIIHH", stampNs, frameId, jpegLength, width, height)
```

| 필드 | 크기 | 의미 |
| --- | --- | --- |
| `stampNs` | 8 bytes | Jetson에서 만든 프레임 시각 |
| `frameId` | 4 bytes | 프레임 번호 |
| `jpegLength` | 4 bytes | JPEG 데이터 크기 |
| `width` | 2 bytes | GUI 송출 영상 너비 |
| `height` | 2 bytes | GUI 송출 영상 높이 |

헤더 뒤에는 실제 JPEG 바이트가 붙습니다.

GUI는 이 JPEG를 디코딩해서 EO 카메라 영역에 표시합니다.

### 4-3. Detection 패킷

탐지 결과 패킷은 다음 구조입니다.

```text
DETS + JSON
```

JSON 예시는 다음과 같습니다.

```json
{
  "stampNs": 123456789,
  "frameId": 120,
  "width": 1280,
  "height": 720,
  "detections": [
    {
      "className": "person",
      "score": 0.91,
      "x1": 100.0,
      "y1": 120.0,
      "x2": 180.0,
      "y2": 300.0,
      "objectId": 1
    }
  ]
}
```

여기서 `width`, `height`는 detection 좌표가 기준으로 삼는 영상 크기입니다. GUI는 이 좌표계를 기준으로 현재 화면 크기에 맞게 바운딩 박스를 그립니다.

### 4-4. Status 패킷

YOLO 상태 패킷은 다음 구조입니다.

```text
STAT + JSON
```

포함 정보는 다음과 같습니다.

| 필드 | 의미 |
| --- | --- |
| `enabled` | YOLO 기능 활성 여부 |
| `modelLoaded` | 모델 로드 여부 |
| `confThreshold` | confidence 기준값 |
| `lastError` | 마지막 오류 메시지 |
| `source` | 영상 소스 경로 |
| `stampNs` | 상태 시각 |
| `frameId` | 관련 프레임 번호 |

## 5. 현재 기본 송출 설정

현재 실행 스크립트의 기본값은 다음과 같습니다.

| 설정 | 기본값 | 의미 |
| --- | --- | --- |
| `GUI_HOST` | `192.168.1.94` | 운용통제 PC IP |
| `GUI_PORT` | `5000` | GUI UDP 수신 포트 |
| `SOURCE_ROOT` | `/data/MEVA` | 컨테이너 내부 MEVA 경로 |
| `HOST_MEVA_PATH` | `$HOME/datashets/MEVA` | Jetson 실제 MEVA 경로 |
| `STREAM_MAX_WIDTH` | `854` | GUI 송출 최대 너비 |
| `STREAM_MAX_HEIGHT` | `480` | GUI 송출 최대 높이 |
| `JPEG_QUALITY` | `45` | JPEG 품질 |
| `MAX_UDP_BYTES` | `55000` | UDP payload 목표 크기 |
| `INFERENCE_SIZE` | `640` | YOLO 입력 크기 |
| `DETECTION_INTERVAL_SECONDS` | `0.5` | YOLO 추론 주기 |
| `ENABLE_ASYNC_ENCODING` | `true` | JPEG 인코딩 쓰레드 사용 |
| `ENABLE_ASYNC_UDP_SEND` | `true` | UDP 전송 쓰레드 사용 |
| `UDP_SEND_BUFFER_BYTES` | `4194304` | UDP 송신 버퍼 목표 크기 |

## 6. Docker 이미지

Dockerfile 기준 베이스 이미지는 다음과 같습니다.

```dockerfile
ARG BASE_IMAGE=ultralytics/ultralytics:latest-nvidia-arm64
FROM ${BASE_IMAGE}
```

YOLO 모델은 기본적으로 `yolo11s.pt`를 이미지 빌드 시 다운로드해서 `/opt/models`에 저장합니다.

```dockerfile
ARG MODEL_NAME=yolo11s.pt
ENV MODEL_PATH=/opt/models/${MODEL_NAME}
```

이미지 이름은 실행 스크립트에서 기본적으로 다음 이름을 사용합니다.

```text
meva-yolo-demo
```

## 7. Docker 명령어

실행 스크립트가 내부적으로 사용하는 핵심 Docker 실행 형태는 다음과 같습니다.

```bash
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST="$GUI_HOST" \
  -e GUI_PORT="$GUI_PORT" \
  -e SOURCE_ROOT="$SOURCE_ROOT" \
  -e CLIP_START_SECONDS="$CLIP_START_SECONDS" \
  -e CLIP_DURATION_SECONDS="$CLIP_DURATION_SECONDS" \
  -e SAMPLE_INTERVAL_SECONDS="$SAMPLE_INTERVAL_SECONDS" \
  -e SAMPLE_START_RATIO="$SAMPLE_START_RATIO" \
  -e CONFIDENCE="$CONFIDENCE" \
  -e DETECTION_INTERVAL_SECONDS="$DETECTION_INTERVAL_SECONDS" \
  -e INFERENCE_SIZE="$INFERENCE_SIZE" \
  -e JPEG_QUALITY="$JPEG_QUALITY" \
  -e MAX_UDP_BYTES="$MAX_UDP_BYTES" \
  -e STREAM_MAX_WIDTH="$STREAM_MAX_WIDTH" \
  -e STREAM_MAX_HEIGHT="$STREAM_MAX_HEIGHT" \
  -e STREAM_TARGET_FPS="$STREAM_TARGET_FPS" \
  -e ENABLE_FRAME_SKIP="$ENABLE_FRAME_SKIP" \
  -e MAX_FRAME_SKIP="$MAX_FRAME_SKIP" \
  -e INFERENCE_SOURCE_MAX_WIDTH="$INFERENCE_SOURCE_MAX_WIDTH" \
  -e INFERENCE_SOURCE_MAX_HEIGHT="$INFERENCE_SOURCE_MAX_HEIGHT" \
  -e ENABLE_ASYNC_ENCODING="$ENABLE_ASYNC_ENCODING" \
  -e ENABLE_ASYNC_UDP_SEND="$ENABLE_ASYNC_UDP_SEND" \
  -e UDP_SEND_BUFFER_BYTES="$UDP_SEND_BUFFER_BYTES" \
  -e ENABLE_TILE_INFERENCE="$ENABLE_TILE_INFERENCE" \
  -v "$HOST_MEVA_PATH:$SOURCE_ROOT:ro" \
  "$IMAGE_NAME"
```

중요 옵션은 다음과 같습니다.

| 옵션 | 의미 |
| --- | --- |
| `--runtime nvidia` | Jetson GPU/NVIDIA 런타임 사용 |
| `--network host` | 컨테이너가 Jetson 호스트 네트워크를 그대로 사용 |
| `-v "$HOST_MEVA_PATH:$SOURCE_ROOT:ro"` | Jetson MEVA 폴더를 컨테이너에 읽기 전용 연결 |
| `-e GUI_HOST` | GUI PC IP 전달 |
| `-e GUI_PORT` | GUI UDP 포트 전달 |

## 8. 프로그램 실행 명령어

### 8-1. GUI 실행

운용통제 PC에서 실행합니다.

```powershell
cd "C:\Users\buguen\Documents\New project"
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

GUI는 UDP `5000` 포트에서 Jetson 영상 패킷을 기다립니다.

### 8-2. Jetson 접속

Windows CMD에서 Jetson으로 SSH 접속합니다.

```cmd
ssh lig@192.168.3.143
```

비밀번호는 다음과 같습니다.

```text
lig
```

### 8-3. Jetson 최신 브랜치 실행

Jetson SSH 접속 후 실행합니다.

```bash
cd ~/LIG_DNA_GUI
git fetch origin
git switch 2026_04_21_ver2/stream-thread-pipeline
git pull

cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_meva_yolo_demo.sh --build
```

이미 Docker 이미지를 빌드한 뒤에는 다음처럼 실행만 하면 됩니다.

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_meva_yolo_demo.sh
```

### 8-4. 화질을 조금 올려서 실행

현재보다 화질을 올려 테스트할 때는 다음처럼 실행합니다.

```bash
STREAM_MAX_WIDTH=960 STREAM_MAX_HEIGHT=540 JPEG_QUALITY=50 MAX_UDP_BYTES=60000 bash ./run_meva_yolo_demo.sh
```

UDP 버퍼까지 늘려서 테스트하려면 다음처럼 실행합니다.

```bash
UDP_SEND_BUFFER_BYTES=4194304 STREAM_MAX_WIDTH=960 STREAM_MAX_HEIGHT=540 JPEG_QUALITY=50 MAX_UDP_BYTES=60000 bash ./run_meva_yolo_demo.sh
```

### 8-5. 쓰레드 기능 비교 실행

인코딩 쓰레드와 UDP 전송 쓰레드를 모두 끄고 비교하려면 다음처럼 실행합니다.

```bash
ENABLE_ASYNC_ENCODING=false ENABLE_ASYNC_UDP_SEND=false bash ./run_meva_yolo_demo.sh
```

UDP 전송 쓰레드만 끄려면 다음처럼 실행합니다.

```bash
ENABLE_ASYNC_UDP_SEND=false bash ./run_meva_yolo_demo.sh
```

## 9. 한 줄 정리

현재 마지막 버전은 ROS2 토픽이 아니라 UDP 패킷 기반 구조입니다. Jetson 쪽에서는 메인 루프, YOLO 쓰레드, JPEG 인코딩 쓰레드, UDP 전송 쓰레드로 작업을 나누어 영상 송출 끊김을 줄이도록 설계되어 있습니다.
