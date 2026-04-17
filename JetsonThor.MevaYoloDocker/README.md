# JetsonThor.MevaYoloDocker

Jetson Thor에서 Docker로 실행하는 MEVA 데모영상 YOLO 처리 서비스입니다.

이 서비스는 다음 순서로 동작합니다.

1. `~/datashets/MEVA` 아래의 데모영상을 읽습니다.
2. YOLO로 객체 탐지와 추적을 수행합니다.
3. 바운딩 박스와 작은 라벨을 프레임 위에 그립니다.
4. 라벨은 `person object1`, `car object2`처럼 표시됩니다.
5. 처리된 프레임을 UDP JPEG 형식으로 운용통제 GUI에 전송합니다.

## 폴더 역할

- `BroadcastControl.MevaDemo.App`
  - 기존 GUI를 복제한 데모 전용 앱입니다.
  - Jetson이 보내는 YOLO 처리 영상을 EO 화면에 표시합니다.
- `JetsonThor.MevaYoloDocker`
  - Jetson Thor에서 Docker로 실행할 YOLO 처리 서비스입니다.

## GUI 실행

운용통제 PC에서 데모 GUI를 실행합니다.

```powershell
dotnet build BroadcastControl.MevaDemo.slnx -c Debug
dotnet run --project .\BroadcastControl.MevaDemo.App\BroadcastControl.MevaDemo.App.csproj
```

GUI는 기본적으로 UDP 포트 `5000`에서 Jetson이 보내는 영상을 기다립니다.

## Jetson에서 Docker 빌드

Jetson Thor에 SSH 접속 후 아래 순서로 진행합니다.

```bash
cd ~/path/to/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
sudo docker build -t meva-yolo-demo .
```

기본 베이스 이미지는 `ultralytics/ultralytics:latest-nvidia-arm64`입니다.
필요하면 빌드 시 `--build-arg BASE_IMAGE=...`로 바꿀 수 있습니다.

현재 Docker 빌드 단계에서 아래 두 가지를 미리 처리합니다.

- `lap>=0.5.12` 설치
- `yolo11n.pt` 모델 다운로드

즉, 한 번 이미지를 만들고 나면 컨테이너 실행 시마다 다시 모델을 받거나
추적 패키지를 설치하지 않아도 됩니다.

다른 모델을 이미지에 포함하고 싶으면:

```bash
sudo docker build --build-arg MODEL_NAME=yolo11s.pt -t meva-yolo-demo .
```

## Jetson에서 Docker 실행

아래 예시는 `~/datashets/MEVA`를 컨테이너 안의 `/data/MEVA`로 마운트합니다.
`GUI_HOST`에는 운용통제 PC의 IP를 넣어야 합니다.

```bash
sudo docker run --rm \
  --runtime nvidia \
  --network host \
  -e GUI_HOST=192.168.0.10 \
  -e GUI_PORT=5000 \
  -e SOURCE_ROOT=/data/MEVA \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

## 환경변수

- `GUI_HOST`
  - 운용통제 PC IP
- `GUI_PORT`
  - GUI가 듣고 있는 UDP 포트, 기본값 `5000`
- `SOURCE_ROOT`
  - MEVA 영상 루트 폴더
- `VIDEO_PATH`
  - 특정 파일만 지정하고 싶을 때 사용
- `MODEL_PATH`
  - 사용할 YOLO 모델 파일 또는 모델명
  - 기본값은 Docker 이미지 안에 포함된 `/opt/models/yolo11n.pt`
- `CONFIDENCE`
  - 탐지 confidence threshold
- `JPEG_QUALITY`
  - 전송 JPEG 품질
- `LOOP_VIDEO`
  - 샘플 구간 재생이 끝난 뒤 다시 처음 구간부터 반복할지 여부
- `CLIP_START_SECONDS`
  - 첫 샘플 구간 시작 시점(초)
- `CLIP_DURATION_SECONDS`
  - 한 번에 재생할 샘플 구간 길이(초)
- `SAMPLE_INTERVAL_SECONDS`
  - 샘플 구간 시작 간격(초)
  - 기본값 `43200` = 12시간마다
- `BOX_THICKNESS`
  - 바운딩 박스 두께
- `FONT_SCALE`
  - 라벨 글자 크기
- `LABEL_THICKNESS`
  - 라벨 글자 두께

## 객체 라벨 방식

객체 옆에 다음과 같은 형식으로 작은 글씨를 붙입니다.

- `person object1`
- `car object2`
- `truck object3`

추적 ID가 있으면 그 값을 `objectN`에 사용하고,
없으면 프레임 내 순서로 번호를 붙입니다.

## 12시간마다 15초씩 재생하기

기본 동작은 다음과 같습니다.

- 12시간마다
- 15초씩
- 순서대로 재생 후 다시 첫 구간부터 반복

즉 영상이 길다면 아래처럼 샘플 구간만 재생합니다.

- 0시간 00분 00초 ~ 0시간 00분 15초
- 12시간 00분 00초 ~ 12시간 00분 15초
- 24시간 00분 00초 ~ 24시간 00분 15초

예를 들어 12시간 간격으로 15초씩 확인하려면:

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
  -e BOX_THICKNESS=3 \
  -e FONT_SCALE=0.75 \
  -e LABEL_THICKNESS=2 \
  -v ~/datashets/MEVA:/data/MEVA:ro \
  meva-yolo-demo
```

이렇게 하면 긴 원본 전체를 보지 않고,
12시간 간격 샘플 구간만 반복 재생하면서
바운딩 박스와 라벨을 더 쉽게 확인할 수 있습니다.

## SSH 사용 예시

```bash
ssh lig@<jetson-ip>
cd ~/datashets
ls
```

`MEVA` 폴더가 보이면 위 Docker 실행 예시처럼 마운트해서 사용할 수 있습니다.

## 참고

Jetson GPU가 있는 ARM64 환경에서는 Ultralytics의 NVIDIA ARM64 Docker 이미지를 우선 사용하고,
Jetson/Docker GPU 설정은 NVIDIA Container Toolkit이 준비되어 있어야 합니다.

공식 참고:
- https://github.com/ultralytics/ultralytics
- https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/
