# JetsonThor.MevaYoloDocker

Jetson Thor에서 Docker로 실행하는 MEVA 데모영상 YOLO 처리 서비스입니다.

이 서비스는 다음 순서로 동작합니다.

1. `~/datashets/MEVA` 아래의 영상 파일들을 찾습니다.
2. 파일명에 들어 있는 시작 시각을 읽어 시간순으로 정렬합니다.
3. 첫 영상 시점을 기준으로 12시간 간격 샘플 파일 목록을 만듭니다.
4. 각 샘플 파일에서 지정된 길이만큼 실제 영상을 재생합니다.
5. YOLO로 객체 탐지와 추적을 수행합니다.
6. 바운딩 박스와 라벨을 프레임 위에 그립니다.
7. 같은 UDP `5000` 포트로 구간 시작 메타데이터 패킷을 먼저 보내고, 이어서 JPEG 프레임을 계속 전송합니다.

## 역할

- `BroadcastControl.MevaDemo.App`
  - 운용통제 PC에서 실행되는 데모 GUI입니다.
  - Jetson 컨테이너가 보내는 YOLO 처리 영상을 EO 화면에 표시합니다.
  - 같은 UDP 포트로 들어오는 메타데이터 패킷을 읽어 시스템 로그에 표시합니다.
- `JetsonThor.MevaYoloDocker`
  - Jetson Thor에서 Docker로 실행되는 YOLO 처리 서비스입니다.
  - MEVA 폴더 전체에서 시간순 샘플 파일을 골라 실제 영상을 재생합니다.

## 중요한 동작 방식

이제는 `MEVA` 폴더의 첫 번째 파일 하나만 반복 재생하지 않습니다.
파일명에 포함된 시각을 기준으로 샘플 파일들을 골라, 예를 들면 아래처럼 재생합니다.

- 첫 번째 샘플: 기준 시각의 파일
- 두 번째 샘플: 12시간 이상 지난 다음 파일
- 세 번째 샘플: 다시 12시간 이상 지난 다음 파일

즉 `12시간마다 15초씩`이라는 뜻은
한 파일 안에서 12시간 뒤를 찾는 것이 아니라,
`MEVA` 데이터셋 전체에서 12시간 간격의 다른 파일들을 선택해 각각 15초씩 재생하는 방식입니다.

## GUI 실행

운용통제 PC에서 데모 GUI를 실행합니다.

```powershell
dotnet build BroadcastControl.MevaDemo.slnx -c Debug
dotnet run --project .\BroadcastControl.MevaDemo.App\BroadcastControl.MevaDemo.App.csproj
```

GUI는 UDP `5000` 포트에서 JPEG 영상과 메타데이터 패킷을 함께 받습니다.

## Jetson에서 Docker 빌드

Jetson Thor에 SSH 접속 후 아래 순서로 진행합니다.

```bash
cd ~/path/to/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker build -t meva-yolo-demo .
```

기본 베이스 이미지는 `ultralytics/ultralytics:latest-nvidia-arm64`입니다.

```bash
sudo docker build --build-arg BASE_IMAGE=<your-base-image> -t meva-yolo-demo .
```

현재 Docker 빌드 단계에서 아래 항목을 미리 포함합니다.

- `lap>=0.5.12` 설치
- `yolo11n.pt` 모델 다운로드

즉 이미지가 한 번 만들어지면 컨테이너 실행 때마다 다시 모델을 내려받거나 추적 패키지를 설치하지 않습니다.

## Jetson에서 Docker 실행

아래 예시는 `~/datashets/MEVA`를 컨테이너 안 `/data/MEVA`로 마운트합니다.
`GUI_HOST`에는 운용통제 PC IP를 넣습니다.

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
  -e JPEG_QUALITY=60 \
  -e MAX_UDP_BYTES=60000 \
  -e BOX_THICKNESS=3 \
  -e FONT_SCALE=0.75 \
  -e LABEL_THICKNESS=2 \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

## 환경변수

- `GUI_HOST`
  - 운용통제 PC IP
- `GUI_PORT`
  - GUI가 YOLO 처리 영상과 메타데이터 패킷을 함께 받는 UDP 포트, 기본값 `5000`
- `SOURCE_ROOT`
  - MEVA 영상 루트 폴더
- `VIDEO_PATH`
  - 특정 영상 파일 하나만 지정하고 싶을 때 사용
  - 이 값을 주면 폴더 전체 샘플링 대신 해당 파일 하나만 사용합니다
- `MODEL_PATH`
  - 사용할 YOLO 모델 경로 또는 모델명
  - 기본값은 Docker 이미지 안에 포함된 `/opt/models/yolo11n.pt`
- `CONFIDENCE`
  - 탐지 confidence threshold
- `JPEG_QUALITY`
  - 전송 JPEG 품질
- `MAX_UDP_BYTES`
  - UDP 한 패킷으로 보낼 최대 JPEG 크기 목표
  - 기본값 `60000`
  - 프레임이 너무 크면 품질과 해상도를 자동으로 낮춰 이 크기 안으로 맞춥니다
- `LOOP_VIDEO`
  - 샘플 파일 재생이 끝난 뒤 다시 처음 샘플부터 반복할지 여부
- `CLIP_START_SECONDS`
  - 각 샘플 파일에서 재생을 시작할 시점(초)
- `CLIP_DURATION_SECONDS`
  - 각 샘플 파일에서 재생할 길이(초)
- `SAMPLE_INTERVAL_SECONDS`
  - 다음 샘플 파일을 고를 때 필요한 최소 시간 간격(초)
  - 기본값 `43200` = 12시간
- `BOX_THICKNESS`
  - 바운딩 박스 두께
- `FONT_SCALE`
  - 객체 라벨 글자 크기
- `LABEL_THICKNESS`
  - 객체 라벨 글자 두께

## 객체 라벨 방식

객체 옆에 다음과 같은 형식으로 작은 글씨 라벨을 붙입니다.

- `person object1`
- `car object2`
- `truck object3`

추적 ID가 있으면 그 값을 `objectN`에 사용하고,
없으면 현재 프레임 안에서 보이는 순서대로 번호를 붙입니다.

## 시스템 로그 예시

GUI 시스템 로그에는 아래와 비슷한 메시지가 표시됩니다.

- `MEVA video segment changed: clip 1/3 now playing 00:00:00 ~ 00:00:15`
- `MEVA video segment changed: clip 2/3 now playing 12:00:00 ~ 12:00:15`
- `MEVA video segment replay restarted: clip 1/3 now replaying 00:00:00 ~ 00:00:15`

즉 로그의 `12:00:00`은 한 파일 안의 12시간이 아니라,
데이터셋 시작 시점 기준으로 12시간 뒤에 해당하는 다른 샘플 파일을 의미합니다.

## SSH 예시

```bash
ssh lig@192.168.3.143
cd ~/datashets
ls
```

`MEVA` 폴더가 보이면 Docker 실행 예시처럼 마운트해서 사용할 수 있습니다.

## 참고

Jetson GPU가 있는 ARM64 환경에서는 Ultralytics의 NVIDIA ARM64 기반 이미지를 쓰는 것이 편하고,
Jetson/Docker GPU 설정은 NVIDIA Container Toolkit이 준비되어 있어야 합니다.

공식 참고:
- https://github.com/ultralytics/ultralytics
- https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/
