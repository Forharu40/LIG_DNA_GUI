# MEVA GUI 실행 및 연동 정리

## 1. 지금 당장 실행할 명령어

### 운용통제 PC

```powershell
cd "C:\Users\buguen\Documents\New project"
dotnet build .\BroadcastControl.slnx -c Debug -p:UseAppHost=false
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

### Jetson Thor

현재 운용통제 PC IP가 `192.168.1.94` 이므로 `GUI_HOST`는 아래처럼 맞춰서 실행합니다.

`SAMPLE_START_RATIO=0.5` 는 샘플 목록의 50% 이후부터 재생을 시작한다는 뜻입니다.
현재 테스트 기본값은 `80% 지점부터 시작`, `30분 간격`, `10초 재생`입니다.

```bash
cd ~/LIG_DNA_GUI
git pull

cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker build -t meva-yolo-demo .
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST=192.168.1.94 \
  -e GUI_PORT=5000 \
  -e SOURCE_ROOT=/data/MEVA \
  -e CLIP_START_SECONDS=0 \
  -e CLIP_DURATION_SECONDS=10 \
  -e SAMPLE_INTERVAL_SECONDS=1800 \
  -e SAMPLE_START_RATIO=0.8 \
  -e JPEG_QUALITY=45 \
  -e MAX_UDP_BYTES=55000 \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

## 2. 이후 실행만 할 때 명령어

### 운용통제 PC

```powershell
cd "C:\Users\buguen\Documents\New project"
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

### Jetson Thor

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST=192.168.1.94 \
  -e GUI_PORT=5000 \
  -e SOURCE_ROOT=/data/MEVA \
  -e CLIP_START_SECONDS=0 \
  -e CLIP_DURATION_SECONDS=10 \
  -e SAMPLE_INTERVAL_SECONDS=1800 \
  -e SAMPLE_START_RATIO=0.8 \
  -e JPEG_QUALITY=45 \
  -e MAX_UDP_BYTES=55000 \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

## 3. GUI 연동 명세서 반영 상태

기준 문서:
- `C:\Users\buguen\Downloads\GUI_연동_명세서.md`

현재 프로젝트는 명세서 방향에 맞춰 아래처럼 반영되었습니다.

### 현재 반영된 항목

1. 원본 영상과 탐지 결과 분리
- Jetson은 더 이상 바운딩 박스를 영상에 직접 그려서 보내지 않습니다.
- 원본 프레임은 영상 패킷으로 보내고, 탐지 결과는 detection 패킷으로 따로 보냅니다.

2. `stamp` 성격의 프레임 시간값 포함
- Jetson이 GUI로 보내는 영상 패킷 헤더에 `uint64 frame_stamp_ns`를 넣습니다.
- detection 패킷과 status 패킷에도 같은 프레임 기준 시간값을 넣습니다.

3. `frame_id` 성격의 프레임 번호 포함
- Jetson이 GUI로 보내는 영상 패킷 헤더에 `uint32 frame_index`를 넣습니다.
- detection/status 패킷에도 같은 `frameId`를 넣습니다.

4. GUI에서 영상과 detection 매칭
- GUI는 `frameId` 기준으로 현재 프레임과 detection 패킷을 매칭합니다.
- detection 박스와 라벨은 GUI가 직접 EO 화면 위에 그립니다.

5. YOLO 상태 패킷 분리
- Jetson은 YOLO enabled/modelLoaded/confThreshold/lastError/source 정보를 status 패킷으로 따로 보냅니다.
- GUI는 이를 받아 상태 이상이 있으면 시스템 로그에 반영합니다.

## 4. 지금 프로젝트에서 실제로 적용된 방식

현재 구조는 아래와 같습니다.

1. Jetson이 MEVA 영상을 읽음
2. YOLO 추론과 추적 수행
3. 원본 프레임은 JPEG로 인코딩
4. detection 배열은 별도 패킷으로 직렬화
5. status 정보도 별도 패킷으로 직렬화
6. Jetson이 세 정보를 같은 UDP 포트 `5000`으로 순차 전송
7. GUI는 원본 프레임을 EO 화면에 표시
8. GUI는 detection 패킷을 받아 화면 위에 바운딩 박스와 라벨을 직접 렌더링
9. GUI는 status 패킷을 받아 상태 이상만 로그에 반영

즉 지금은 이미
- `원본 영상`
- `탐지 결과`
- `상태`
를 논리적으로 분리한 구조입니다.

## 5. Jetson에서 GUI로 보내는 패킷 방식

현재 Jetson은 GUI로 같은 UDP `5000` 포트에 세 종류의 패킷을 보냅니다.

### 5.1 영상 패킷

구조:

```text
[20바이트 헤더] + [JPEG 바이트]
```

헤더 포맷:

```text
!QIIHH
```

의미:

- `Q`: `uint64 frame_stamp_ns`
- `I`: `uint32 frame_index`
- `I`: `uint32 image_byte_length`
- `H`: `uint16 width`
- `H`: `uint16 height`

### 5.2 detection 패킷

구조:

```text
[4바이트 magic = DETS] + [UTF-8 JSON]
```

JSON 필드:

- `stampNs`
- `frameId`
- `width`
- `height`
- `detections[]`

각 detection 항목:

- `className`
- `score`
- `x1`
- `y1`
- `x2`
- `y2`
- `objectId`

### 5.3 status 패킷

구조:

```text
[4바이트 magic = STAT] + [UTF-8 JSON]
```

JSON 필드:

- `enabled`
- `modelLoaded`
- `confThreshold`
- `lastError`
- `source`
- `stampNs`
- `frameId`

### 5.4 구간 메타데이터 패킷

구조:

```text
36바이트 고정 길이 패킷
```

포맷:

```text
>4sIHHIIIIII
```

필드 의미:

- `4s`: magic = `MEVA`
- `I`: image_byte_length = `0`
- `H`: width = `0`
- `H`: height = `0`
- `I`: clip_index
- `I`: clip_count
- `I`: segment_start_seconds
- `I`: segment_end_seconds
- `I`: current_playback_seconds
- `I`: cycle_index

## 6. 샘플 시작 위치

현재는 `SAMPLE_START_RATIO`로 샘플 목록 어디서부터 시작할지 정할 수 있습니다.

- `0.0`: 샘플 목록 처음부터 시작
- `0.5`: 샘플 목록 50% 이후부터 시작
- `0.8`: 샘플 목록 80% 이후부터 시작
- `1.0`: 마지막 샘플부터 시작

예를 들어 샘플이 10개면 `0.8`일 때 9번째 샘플부터 시작하고, 끝까지 간 뒤 다시 앞부분을 재생합니다.

## 7. 지금 GUI에서 정상일 때 보이는 로그

정상 수신이라면 시스템 로그에 아래 흐름이 보여야 합니다.

```text
MEVA YOLO UDP stream receiver is waiting on port 5000.
MEVA UDP 첫 패킷을 수신했습니다. 송신지: <jetson-ip>:<port>, 패킷 크기: ...
MEVA UDP 구간 메타데이터 패킷을 수신했습니다.
EO UDP camera first frame received.
MEVA video segment changed: clip 1/8 now playing ...
```

## 8. 이후 확장 방향

지금 구조는 이후 기능 확장을 고려한 방향으로 바뀌었습니다.

다음 단계로 자연스럽게 이어질 수 있는 작업:

1. GUI에서 객체 클릭 좌표를 detection 박스와 매칭
2. 선택된 객체만 별도 강조 표시
3. 선택 객체 기준으로 Jetson에 후속 YOLO/VLM 요청 전송
4. status 패널에 YOLO 상태를 시각적으로 표시

즉 현재 구조는 이후 `GUI에서 원하는 객체 선택 -> YOLO/VLM 후속 처리`를 붙이기 위한 기반 구조입니다.
