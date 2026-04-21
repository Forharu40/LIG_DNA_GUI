# MEVA 영상 송출 및 실행 구조 정리

이 문서는 현재 프로젝트에서

- Jetson에서 GUI로 영상을 어떤 방식으로 보내는지
- 패킷 헤더와 payload가 어떤 상태인지
- 운용통제 GUI는 무엇을 실행하는지
- Jetson에서는 어떤 프로그램을 실행하는지
- Jetson 쪽 Python 파일이 프로젝트 어디에 있는지

를 한 번에 확인할 수 있도록 정리한 문서입니다.

## 1. 전체 구조

현재 구조는 아래와 같습니다.

1. Jetson 쪽 Docker 컨테이너에서 `stream_meva_yolo.py`가 실행됩니다.
2. 이 Python 프로그램이 `MEVA` 데모 영상을 읽습니다.
3. 프레임을 JPEG로 인코딩하고, YOLO detection 결과도 같이 만듭니다.
4. Jetson은 운용통제 PC GUI로 UDP 패킷을 보냅니다.
5. GUI는 `UdpEncodedVideoReceiverService.cs`에서 이 패킷을 수신합니다.
6. GUI는 EO 화면에 영상을 표시하고, detection 정보로 바운딩 박스를 그립니다.

즉, 현재는:

- Jetson: 영상 송신 + YOLO 추론 + detection 데이터 전송
- GUI: 영상 수신 + detection 해석 + 화면 표시

구조입니다.

---

## 2. Jetson에서 GUI로 보내는 패킷 구조

Jetson의 핵심 송신 파일:

- [stream_meva_yolo.py](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/app/stream_meva_yolo.py>)

GUI의 핵심 수신 파일:

- [UdpEncodedVideoReceiverService.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs>)

현재 UDP `5000` 포트로 여러 종류의 패킷을 보냅니다.

### 2-1. 구간 메타데이터 패킷

Jetson 코드:

```python
def build_video_packet(
    clip_index: int,
    clip_count: int,
    segment_start_seconds: float,
    segment_end_seconds: float,
    current_playback_seconds: float,
    cycle_index: int,
) -> bytes:
    return struct.pack(
        ">4sIHHIIIIII",
        b"MEVA",
        0,
        0,
        0,
        clip_index,
        clip_count,
        int(max(0, segment_start_seconds)),
        int(max(0, segment_end_seconds)),
        int(max(0, current_playback_seconds)),
        max(0, cycle_index),
    )
```

형식:

- 크기: `36 bytes`
- 엔디언: `big-endian`
- 매직: `MEVA`

구성:

1. `4s`: `"MEVA"`
2. `I`: reserved
3. `H`: reserved
4. `H`: reserved
5. `I`: `clip_index`
6. `I`: `clip_count`
7. `I`: `segment_start_seconds`
8. `I`: `segment_end_seconds`
9. `I`: `current_playback_seconds`
10. `I`: `cycle_index`

역할:

- 지금 어떤 샘플 구간을 재생 중인지 GUI에 알려줍니다.
- GUI 시스템 로그의 `MEVA video segment changed: ...` 메시지가 이 패킷을 기준으로 나옵니다.

---

### 2-2. 영상 프레임 패킷

Jetson 코드:

```python
def build_image_packet(
    encoded_bytes: bytes,
    width: int,
    height: int,
    frame_index: int,
    frame_stamp_ns: int,
) -> bytes:
    header = struct.pack(
        "!QIIHH",
        max(0, frame_stamp_ns),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, width),
        max(0, height),
    )
    return header + encoded_bytes
```

형식:

- 헤더 크기: `20 bytes`
- 엔디언: network byte order (`!`)
- 뒤에 JPEG 바이트가 이어짐

헤더 구성:

1. `Q`: `frame_stamp_ns`
2. `I`: `frame_index`
3. `I`: JPEG payload byte length
4. `H`: width
5. `H`: height

그 뒤:

- JPEG 인코딩된 실제 프레임 데이터

역할:

- GUI EO 화면에 표시할 원본 영상 프레임을 전달합니다.

현재 프로젝트에서는 영상 프레임을 보내기 전에 JPEG 품질과 해상도를 조절해 UDP 패킷 크기를 줄입니다.

관련 함수:

- `encode_frame_for_udp(...)`

---

### 2-3. Detection 패킷

Jetson 코드:

```python
def build_detection_packet(
    frame_stamp_ns: int,
    frame_index: int,
    width: int,
    height: int,
    detections: list[dict],
) -> bytes:
    payload = {
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
        "width": max(0, width),
        "height": max(0, height),
        "detections": detections,
    }
    return DETECTION_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")
```

형식:

1. 앞 4바이트: `DETS`
2. 뒤: UTF-8 JSON payload

JSON 예시 구조:

```json
{
  "stampNs": 123456789,
  "frameId": 191,
  "width": 1280,
  "height": 720,
  "detections": [
    {
      "className": "chair",
      "score": 0.91,
      "x1": 100.0,
      "y1": 200.0,
      "x2": 180.0,
      "y2": 320.0,
      "objectId": 1
    }
  ]
}
```

역할:

- YOLO detection 결과를 GUI에 별도로 전달합니다.
- GUI는 이 데이터를 받아 바운딩 박스와 라벨을 그립니다.

즉 현재는:

- 영상 자체에 박스를 Jetson이 그려서 보내는 방식이 아니라
- 영상과 detection 데이터를 분리해서 보내는 방식

입니다.

---

### 2-4. Status 패킷

Jetson 코드:

```python
def build_status_packet(
    enabled: bool,
    model_loaded: bool,
    conf_threshold: float,
    last_error: str,
    source: str,
    frame_stamp_ns: int = 0,
    frame_index: int = 0,
) -> bytes:
    payload = {
        "enabled": enabled,
        "modelLoaded": model_loaded,
        "confThreshold": conf_threshold,
        "lastError": last_error,
        "source": source,
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")
```

형식:

1. 앞 4바이트: `STAT`
2. 뒤: UTF-8 JSON payload

역할:

- YOLO 모델 로딩 여부
- confidence threshold
- 오류 문자열
- source 정보

를 GUI에 알려줍니다.

GUI의 `YOLO 모델이 아직 로드되지 않았습니다.` 같은 로그는 이 패킷을 기준으로 처리됩니다.

---

## 3. GUI는 어떤 프로그램을 실행하나

현재 운용통제 GUI 프로젝트:

- [BroadcastControl.App.csproj](</C:/Users/buguen/Documents/New project/BroadcastControl.App/BroadcastControl.App.csproj>)

실행 명령:

```powershell
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

즉, GUI는 `BroadcastControl.App` 프로젝트를 실행합니다.

관련 핵심 파일:

- 메인 창 XAML: [MainWindow.xaml](</C:/Users/buguen/Documents/New project/BroadcastControl.App/MainWindow.xaml>)
- 메인 창 코드비하인드: [MainWindow.xaml.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/MainWindow.xaml.cs>)
- 상태/바인딩 ViewModel: [MainViewModel.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/ViewModels/MainViewModel.cs>)
- UDP 수신 서비스: [UdpEncodedVideoReceiverService.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs>)

현재 실제 흐름:

1. `MainWindow`가 열리면
2. `UdpEncodedVideoReceiverService.Start()`가 호출되고
3. GUI는 UDP `5000` 포트에서 Jetson 패킷을 기다립니다.

---

## 4. Jetson에서는 어떤 프로그램을 실행하나

Jetson에서는 직접 Python 파일을 실행하는 것이 아니라, Docker 컨테이너를 실행합니다.

Docker 설정 파일:

- [Dockerfile](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/Dockerfile>)

Jetson 쪽 실제 Python 실행 엔트리:

- [stream_meva_yolo.py](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/app/stream_meva_yolo.py>)

Dockerfile 마지막 줄:

```dockerfile
CMD ["python3", "/app/stream_meva_yolo.py"]
```

즉 Jetson에서 Docker 컨테이너가 올라가면, 컨테이너 내부에서는 `python3 /app/stream_meva_yolo.py`가 실행됩니다.

---

## 5. Jetson에서 실행할 때 실제로 쓰는 파일들

### 5-1. 핵심 Python 파일

- [stream_meva_yolo.py](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/app/stream_meva_yolo.py>)

역할:

- MEVA 영상 탐색
- 샘플 구간 선택
- YOLO detection 수행
- JPEG 인코딩
- 구간/영상/detection/status 패킷 전송

### 5-2. Docker 환경 파일

- [Dockerfile](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/Dockerfile>)

역할:

- Ultralytics 기반 이미지 사용
- YOLO 모델 다운로드
- Python 실행 환경 구성
- 기본 실행 명령 정의

### 5-3. Jetson 실행 스크립트

- [run_meva_yolo_demo.sh](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/run_meva_yolo_demo.sh>)

역할:

- 이미지가 없으면 `docker build`
- 이미지가 있으면 바로 `docker run`
- 자주 쓰는 환경변수를 한 번에 전달

즉 Jetson에서는 보통 이 스크립트를 실행하면 됩니다.

---

## 6. 현재 권장 실행 방식

### 6-1. 운용통제 PC

```powershell
cd "C:\Users\buguen\Documents\New project"
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

### 6-2. Jetson

처음 한 번 빌드 포함:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_meva_yolo_demo.sh --build
```

그 이후 보통 실행:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_meva_yolo_demo.sh
```

값 변경 예시:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
GUI_HOST=192.168.1.94 DETECTION_INTERVAL_SECONDS=0.5 bash ./run_meva_yolo_demo.sh
```

---

## 7. 현재 영상 데이터는 어떤 방식으로 이동하나

정리하면 현재는 아래 순서입니다.

1. Jetson Docker 컨테이너 내부의 `stream_meva_yolo.py`가 실행됨
2. `MEVA` 영상 프레임을 읽음
3. 프레임을 JPEG로 인코딩
4. YOLO detection 결과를 JSON으로 구성
5. 아래 패킷들을 UDP `5000`으로 GUI에 전송
   - `MEVA` 메타데이터 패킷
   - `20바이트 헤더 + JPEG` 영상 패킷
   - `DETS + JSON` detection 패킷
   - `STAT + JSON` status 패킷
6. GUI의 `UdpEncodedVideoReceiverService`가 이 패킷들을 구분해서 처리
7. GUI 메인 화면이 EO 영상 표시, 상태 로그 표시, detection 오버레이 표시를 수행

즉, 현재는 `Jetson -> GUI` 단방향 UDP 스트리밍 구조입니다.

---

## 8. 현재 구조의 장점

현재 구조는 이후 기능 확장에 유리합니다.

예를 들어 앞으로

- GUI에서 사용자가 특정 객체 클릭
- 해당 좌표 객체만 선택
- 그 객체를 기준으로 VLM 분석 요청

같은 기능을 만들려면, 지금처럼

- 영상 데이터
- detection 좌표 데이터

가 분리되어 있는 편이 훨씬 좋습니다.

즉 현재 구조는 단순 데모가 아니라, 이후 `객체 선택 -> VLM 분석` 흐름으로 확장하기 좋은 구조입니다.

---

## 9. 관련 파일 위치 요약

### GUI 쪽

- 프로젝트: [BroadcastControl.App](</C:/Users/buguen/Documents/New project/BroadcastControl.App>)
- 실행 대상: [BroadcastControl.App.csproj](</C:/Users/buguen/Documents/New project/BroadcastControl.App/BroadcastControl.App.csproj>)
- 메인 창: [MainWindow.xaml](</C:/Users/buguen/Documents/New project/BroadcastControl.App/MainWindow.xaml>)
- 메인 창 코드: [MainWindow.xaml.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/MainWindow.xaml.cs>)
- ViewModel: [MainViewModel.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/ViewModels/MainViewModel.cs>)
- UDP 수신기: [UdpEncodedVideoReceiverService.cs](</C:/Users/buguen/Documents/New project/BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs>)

### Jetson 쪽

- 폴더: [JetsonThor.MevaYoloDocker](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker>)
- Docker 설정: [Dockerfile](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/Dockerfile>)
- 실행 스크립트: [run_meva_yolo_demo.sh](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/run_meva_yolo_demo.sh>)
- Python 본체: [stream_meva_yolo.py](</C:/Users/buguen/Documents/New project/JetsonThor.MevaYoloDocker/app/stream_meva_yolo.py>)

---

## 10. 한 문장 정리

현재 프로젝트는 Jetson Docker 안의 `stream_meva_yolo.py`가 MEVA 영상을 읽어 `메타데이터 / 영상 / detection / status` 패킷을 UDP로 GUI에 보내고, GUI의 `BroadcastControl.App`이 이를 받아 EO 화면과 시스템 로그에 표시하는 구조입니다.
## Encode, Decode, Network Benchmark

Use this when comparing the current GUI stream quality with a higher resolution stream such as `1280x720`.

On Jetson, generate encode timing results and JPEG samples:

```bash
cd ~/LIG_DNA_GUI
git pull

cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
BENCHMARK_SIZE_A=1280x720 BENCHMARK_SIZE_B=640x360 JPEG_QUALITY=35 bash ./run_meva_yolo_demo.sh --build --benchmark-encode
```

The benchmark writes sample JPEG files to:

```text
~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker/benchmark-output
```

Copy those samples to the operation-control PC:

```powershell
scp -r lig@192.168.3.143:~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker/benchmark-output "C:\Users\buguen\Documents\New project\JetsonThor.MevaYoloDocker\"
```

On the operation-control PC, measure GUI-side decode cost and estimated network burden:

```powershell
cd "C:\Users\buguen\Documents\New project"
dotnet run --project .\BroadcastControl.Benchmarks\BroadcastControl.Benchmarks.csproj -- `
  --a .\JetsonThor.MevaYoloDocker\benchmark-output\sample_1280x720_q35.jpg `
  --b .\JetsonThor.MevaYoloDocker\benchmark-output\sample_640x360_q35.jpg `
  --network-mbps 100
```

The output compares:

- encode time from the Jetson script
- GUI-like JPEG decode time from the PC benchmark
- estimated network serialization burden from JPEG byte size and configured Mbps
