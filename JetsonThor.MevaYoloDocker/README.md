# JetsonThor.MevaYoloDocker

Jetson Thor에서 Docker로 실행하는 MEVA YOLO 데모입니다.

구성은 아래와 같습니다.

- `BroadcastControl.App`
  - 운용통제 PC에서 실행하는 WPF GUI
  - UDP `5000` 포트에서 Jetson이 보내는 YOLO 처리 영상을 받습니다.
  - 같은 UDP 포트로 들어오는 구간 메타데이터를 읽어 시스템 로그에 현재 재생 구간을 표시합니다.
- `JetsonThor.MevaYoloDocker`
  - Jetson Thor에서 Docker 컨테이너로 실행하는 YOLO 처리기
  - `~/datashets/MEVA` 아래 영상을 읽고, 12시간 간격으로 샘플 파일을 골라 각 파일에서 지정한 길이만큼 실제 영상을 재생합니다.
  - 객체에 바운딩 박스와 `person object1` 같은 라벨을 그린 뒤 GUI로 UDP 전송합니다.

## 동작 방식

1. `SOURCE_ROOT` 아래에서 영상 파일을 찾습니다.
2. 파일명에 포함된 시작 시각을 읽어 시간순으로 정렬합니다.
3. 첫 파일을 기준으로 `SAMPLE_INTERVAL_SECONDS` 이상 떨어진 파일들을 샘플 목록으로 고릅니다.
4. 각 샘플 파일에서 `CLIP_START_SECONDS`부터 `CLIP_DURATION_SECONDS`만큼 실제 영상을 재생합니다.
5. YOLO 추적 결과를 프레임 위에 그립니다.
6. 구간 시작 시 메타데이터 패킷을 먼저 보내고, 이후 JPEG 프레임을 계속 UDP로 전송합니다.

## 운용통제 PC 실행

기존 메인 프로젝트만 실행하면 됩니다.

```powershell
dotnet build .\BroadcastControl.slnx -c Debug
dotnet run --project .\BroadcastControl.App\BroadcastControl.App.csproj
```

## Jetson Docker 빌드

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker build -t meva-yolo-demo .
```

기본 베이스 이미지는 `ultralytics/ultralytics:latest-nvidia-arm64` 입니다.

## Jetson Docker 실행

아래 예시는 운용통제 PC IP가 `192.168.2.91`일 때 기준입니다.

```bash
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST=192.168.2.91 \
  -e GUI_PORT=5000 \
  -e SOURCE_ROOT=/data/MEVA \
  -e CLIP_START_SECONDS=0 \
  -e CLIP_DURATION_SECONDS=15 \
  -e SAMPLE_INTERVAL_SECONDS=43200 \
  -e JPEG_QUALITY=45 \
  -e MAX_UDP_BYTES=55000 \
  -e BOX_THICKNESS=3 \
  -e FONT_SCALE=0.75 \
  -e LABEL_THICKNESS=2 \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

## 주요 환경변수

- `GUI_HOST`: 운용통제 PC IP
- `GUI_PORT`: GUI가 수신하는 UDP 포트, 기본값 `5000`
- `SOURCE_ROOT`: MEVA 영상 루트 폴더
- `VIDEO_PATH`: 특정 영상 파일 하나만 사용할 때 지정
- `MODEL_PATH`: YOLO 모델 경로, 기본값 `/opt/models/yolo11n.pt`
- `CONFIDENCE`: 탐지 confidence threshold
- `JPEG_QUALITY`: JPEG 품질
- `MAX_UDP_BYTES`: UDP 한 패킷 최대 크기 목표값, 기본값 `60000`
- `LOOP_VIDEO`: 샘플 구간을 다시 처음부터 반복할지 여부
- `CLIP_START_SECONDS`: 각 샘플 파일에서 시작할 초
- `CLIP_DURATION_SECONDS`: 각 샘플 파일에서 재생할 길이(초)
- `SAMPLE_INTERVAL_SECONDS`: 샘플 파일 간 최소 시간 간격(초), 기본값 `43200` = 12시간
- `BOX_THICKNESS`: 바운딩 박스 두께
- `FONT_SCALE`: 라벨 글씨 크기
- `LABEL_THICKNESS`: 라벨 글씨 두께

## GUI 시스템 로그 예시

- `MEVA YOLO UDP stream receiver is waiting on port 5000.`
- `MEVA UDP 첫 패킷을 수신했습니다. 패킷 크기: 32 bytes`
- `MEVA UDP 구간 메타데이터 패킷을 수신했습니다.`
- `EO UDP camera first frame received.`
- `MEVA video segment changed: clip 1/8 now playing 00:00:00 ~ 00:00:15`

## 참고

- Jetson에서 `docker info`에 `nvidia` runtime이 보여야 합니다.
- GUI에 영상이 안 나오면 먼저 시스템 로그에서
  - 첫 UDP 패킷 수신
  - 메타데이터 패킷 수신
  - EO first frame
  순서가 보이는지 확인하면 원인을 좁히기 쉽습니다.
