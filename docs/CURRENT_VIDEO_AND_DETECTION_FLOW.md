# 현재 영상 수신 및 바운딩 박스 처리 방식

## 1. 전체 구조

현재 구조는 아래와 같습니다.

- Jetson Thor
  - MEVA 데모 영상을 읽음
  - YOLO 추론과 추적 수행
  - 원본 영상 프레임은 JPEG로 인코딩해서 GUI로 전송
  - 탐지 결과는 별도 detection 패킷으로 GUI로 전송
  - YOLO 상태는 별도 status 패킷으로 GUI로 전송

- 운용통제 GUI
  - Jetson이 보낸 원본 영상 프레임을 EO 화면에 표시
  - detection 패킷을 받아 같은 프레임에 맞춰 바운딩 박스와 라벨을 직접 그림
  - status 패킷을 받아 오류나 상태를 로그에 반영

즉, 지금은

- `원본 영상`
- `탐지 결과`
- `상태 정보`

를 분리해서 다루는 구조입니다.

## 2. 영상은 어떻게 송출되고 있는가

Jetson의 `stream_meva_yolo.py`가 MEVA 영상 파일을 읽고 프레임을 꺼냅니다.

그 다음 순서는 아래와 같습니다.

1. 원본 프레임을 읽음
2. JPEG로 인코딩
3. `20바이트 헤더 + JPEG` 형태의 UDP 패킷 생성
4. 운용통제 PC의 UDP `5000` 포트로 전송

### 영상 패킷 형식

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

즉 실제 패킷 구조는 다음과 같습니다.

```text
[20바이트 헤더] + [JPEG 바이트]
```

### GUI 쪽 처리

GUI의 `UdpEncodedVideoReceiverService.cs`가 UDP `5000` 포트에서 이 패킷을 받습니다.

처리 순서는 아래와 같습니다.

1. 헤더를 읽어 `frame_stamp_ns`, `frame_index`, `image_byte_length`, `width`, `height`를 파싱
2. 뒤쪽 JPEG 바이트를 추출
3. OpenCV로 JPEG 디코딩
4. WPF `BitmapSource`로 변환
5. EO 메인 화면에 표시

즉 영상 자체는 Jetson이 “원본 영상 프레임”을 보내고, GUI가 이를 받아 화면에 뿌리는 방식입니다.

## 3. 바운딩 박스는 어떻게 처리되고 있는가

중요한 점은, 지금은 Jetson이 바운딩 박스를 영상에 미리 그려서 보내지 않는다는 것입니다.

예전 방식:

- Jetson이 영상 위에 박스와 라벨을 직접 그림
- 그 결과가 이미 합성된 영상을 GUI로 전송

현재 방식:

- Jetson은 원본 영상만 GUI로 전송
- 탐지 결과 좌표는 별도 detection 패킷으로 전송
- GUI가 detection 패킷을 받아 화면 위에 박스와 라벨을 직접 그림

이 방식이 더 좋은 이유는 이후에 GUI에서 특정 객체를 클릭하거나 선택하는 기능을 붙이기 쉽기 때문입니다.

## 4. detection 패킷 형식

Jetson은 YOLO 결과를 `DETS + JSON` 형태의 UDP 패킷으로 보냅니다.

구조:

```text
[4바이트 magic = DETS] + [UTF-8 JSON]
```

JSON 안에는 아래 값들이 들어갑니다.

- `stampNs`
- `frameId`
- `width`
- `height`
- `detections`

각 detection 항목은 아래 필드를 가집니다.

- `className`
- `score`
- `x1`
- `y1`
- `x2`
- `y2`
- `objectId`

예를 들면 개념적으로 아래와 같습니다.

```json
{
  "stampNs": 1713582000000000000,
  "frameId": 123,
  "width": 1280,
  "height": 720,
  "detections": [
    {
      "className": "person",
      "score": 0.91,
      "x1": 210,
      "y1": 130,
      "x2": 320,
      "y2": 410,
      "objectId": 1
    }
  ]
}
```

## 5. GUI에서 바운딩 박스를 그리는 방식

GUI는 detection 패킷을 받으면 바로 그리지 않고, 현재 EO 영상 프레임과 맞는지 먼저 확인합니다.

현재 기준은 `frameId`입니다.

즉,

- 영상 프레임의 `frame_index`
- detection 패킷의 `frameId`

가 같을 때 같은 프레임으로 간주합니다.

매칭되면 GUI는 `DetectionOverlayCanvas` 위에 아래 요소를 직접 그립니다.

- 사각형 바운딩 박스
- 객체 라벨 텍스트
  - 예: `person object1 (0.91)`

좌표는 원본 영상 기준 좌표를 사용하고, GUI 화면 크기와 줌/이동 상태에 맞게 렌더링 시점에 스케일 변환해서 표시합니다.

즉 박스의 “데이터”는 Jetson이 보내지만, 박스의 “그리기”는 GUI가 담당합니다.

## 6. status 패킷은 어떻게 쓰는가

Jetson은 YOLO 상태를 `STAT + JSON` 패킷으로 보냅니다.

이 안에는 아래 값이 들어갑니다.

- `enabled`
- `modelLoaded`
- `confThreshold`
- `lastError`
- `source`
- `stampNs`
- `frameId`

GUI는 이를 받아

- 모델이 아직 로드되지 않은 경우
- 최근 오류가 있는 경우

시스템 로그에 반영합니다.

## 7. 왜 이 방식으로 바꿨는가

이 구조로 바꾼 가장 큰 이유는, 앞으로 GUI에서 원하는 객체를 직접 선택하는 기능을 넣기 위해서입니다.

예를 들어 이후 목표가 아래와 같다면:

1. 사용자가 EO 화면에서 특정 객체를 클릭
2. GUI가 클릭 좌표와 detection 박스를 비교
3. 선택된 객체를 기준으로 Jetson에 후속 요청
4. YOLO / VLM이 그 객체만 집중 분석

Jetson이 미리 박스를 그린 영상만 보내는 방식은 불리합니다.

반대로 지금처럼

- 원본 영상은 따로
- detection 좌표는 따로

구조로 분리되어 있으면,

- GUI가 어떤 객체가 어느 좌표에 있는지 정확히 알고
- 사용자가 선택한 객체를 식별하고
- 이후 선택 객체 기반 기능으로 확장하기 쉬워집니다.

## 8. 한 문장 요약

현재는 Jetson이 `원본 영상`과 `탐지 결과 좌표`를 따로 보내고, GUI가 이를 받아 EO 화면에 영상을 표시한 뒤 바운딩 박스와 라벨을 직접 그리는 구조입니다.
