# JetsonThor.MevaYoloDocker

Jetson Thor에서 Docker로 실행하는 MEVA 데모영상 YOLO 처리 서비스입니다.

이 서비스는 다음 순서로 동작합니다.

1. `~/datashets/MEVA` 아래의 데모영상을 읽습니다.
2. YOLO로 객체 탐지와 추적을 수행합니다.
3. 바운딩 박스와 라벨을 프레임 위에 그립니다.
4. 라벨은 `person object1`, `car object2`처럼 표시합니다.
5. 처리된 프레임은 UDP JPEG 형식으로 운용통제 GUI에 전송합니다.
6. 구간이 바뀔 때마다 별도 UDP 상태 메시지를 보내 GUI 시스템 로그에 표시합니다.

## 역할

- `BroadcastControl.MevaDemo.App`
  - 운용통제 PC에서 실행되는 데모 GUI입니다.
  - Jetson 컨테이너가 보내는 YOLO 처리 영상을 EO 화면에 표시합니다.
  - Jetson이 보내는 구간 전환 메시지를 시스템 로그에 표시합니다.
- `JetsonThor.MevaYoloDocker`
  - Jetson Thor에서 Docker로 실행되는 YOLO 처리 서비스입니다.
  - MEVA 영상의 지정 구간을 실제 영상으로 재생하면서 탐지 결과를 전송합니다.

## GUI 실행

운용통제 PC에서 데모 GUI를 실행합니다.

```powershell
dotnet build BroadcastControl.MevaDemo.slnx -c Debug
dotnet run --project .\BroadcastControl.MevaDemo.App\BroadcastControl.MevaDemo.App.csproj
```

GUI는 기본적으로 아래 두 포트를 기다립니다.

- UDP `5000`: YOLO 처리 영상 수신
- UDP `5001`: 구간 전환 상태 메시지 수신

## Jetson에서 Docker 빌드

Jetson Thor에 SSH 접속 후 아래 순서로 진행합니다.

```bash
cd ~/path/to/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker build -t meva-yolo-demo .
```

기본 베이스 이미지는 `ultralytics/ultralytics:latest-nvidia-arm64`입니다.
필요하면 아래처럼 바꿀 수 있습니다.

```bash
sudo docker build --build-arg BASE_IMAGE=<your-base-image> -t meva-yolo-demo .
```

현재 Docker 빌드 단계에서 아래 항목을 미리 포함합니다.

- `lap>=0.5.12` 설치
- `yolo11n.pt` 모델 다운로드

즉 이미지가 한 번 만들어지면 컨테이너 실행 때마다 다시 모델을 내려받거나 추적 패키지를 설치하지 않습니다.

다른 YOLO 모델을 이미지에 포함하고 싶으면 아래처럼 빌드합니다.

```bash
sudo docker build --build-arg MODEL_NAME=yolo11s.pt -t meva-yolo-demo .
```

## Jetson에서 Docker 실행

아래 예시는 `~/datashets/MEVA`를 컨테이너 안 `/data/MEVA`로 마운트합니다.
`GUI_HOST`에는 운용통제 PC IP를 넣습니다.

```bash
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST=192.168.2.91 \
  -e GUI_PORT=5000 \
  -e GUI_STATUS_PORT=5001 \
  -e SOURCE_ROOT=/data/MEVA \
  -e CLIP_START_SECONDS=0 \
  -e CLIP_DURATION_SECONDS=15 \
  -e SAMPLE_INTERVAL_SECONDS=43200 \
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
  - GUI가 YOLO 처리 영상을 받는 UDP 포트, 기본값 `5000`
- `GUI_STATUS_PORT`
  - GUI가 구간 전환 로그를 받는 UDP 포트, 기본값 `5001`
- `SOURCE_ROOT`
  - MEVA 영상 루트 폴더
- `VIDEO_PATH`
  - 특정 영상 파일 하나만 지정하고 싶을 때 사용
- `MODEL_PATH`
  - 사용할 YOLO 모델 경로 또는 모델명
  - 기본값은 Docker 이미지 안에 포함된 `/opt/models/yolo11n.pt`
- `CONFIDENCE`
  - 탐지 confidence threshold
- `JPEG_QUALITY`
  - 전송 JPEG 품질
- `LOOP_VIDEO`
  - 샘플 구간 재생이 끝난 뒤 다시 처음 구간부터 반복할지 여부
- `CLIP_START_SECONDS`
  - 첫 번째 샘플 구간 시작 시점(초)
- `CLIP_DURATION_SECONDS`
  - 각 샘플 구간 재생 길이(초)
- `SAMPLE_INTERVAL_SECONDS`
  - 다음 샘플 구간 시작까지의 간격(초)
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

## 12시간마다 15초씩 재생한다는 의미

여기서 말하는 `12시간마다 15초씩`은 정지 화면을 15초 동안 보여주는 뜻이 아닙니다.
해당 시점의 구간을 실제 영상으로 15초 동안 재생한다는 뜻입니다.

즉 기본 동작은 아래와 같습니다.

- `00:00:00 ~ 00:00:15` 구간을 실제 영상으로 재생
- `12:00:00 ~ 12:00:15` 구간을 실제 영상으로 재생
- `24:00:00 ~ 24:00:15` 구간을 실제 영상으로 재생
- 이후 다시 처음 구간부터 반복

구간이 바뀔 때마다 GUI 시스템 로그에는 아래와 비슷한 메시지가 표시됩니다.

- `MEVA video segment changed: clip 1/3 now playing 00:00:00 ~ 00:00:15`
- `MEVA video segment changed: clip 2/3 now playing 12:00:00 ~ 12:00:15`

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
