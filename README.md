# LIG_DNA_GUI

WPF + Material Design 기반의 EO / IR 운용 GUI 프로토타입입니다.

## 한국어

### 화면 구성

- 상단: EO / IR 듀얼 화면
- 중앙 스왑 버튼: 큰 화면과 작은 화면의 카메라 위치 교체
- EO 창: 현재는 노트북 카메라 입력을 사용해 GUI 동작을 검증
- 하단 좌측: VLM 결과값 출력창
- 하단 중앙: 실시간 시스템 로그 창
- 하단 우측: 기능 제어 버튼 및 상태 패널

### 기능 제어 항목

1. 모드
   - 기체고정
   - 수동모드
   - 추적모드
2. 탐지
   - 드론 탐지
   - 고정익 탐지
   - 사람 탐지
   - 복합 탐지
3. On / Off
4. zoom
   - 전자 zoom은 좌측 큰 화면의 표시 배율만 변경
   - 실제 광학 PTZ 제어와는 분리
5. motor
6. tracking
7. 밝기 / 대조비

### 데이터 흐름

1. 카메라 입력
2. 탐지기 / 추적기
3. 이벤트 버퍼
4. VLM 또는 LLM 요약 생성
5. WPF GUI 렌더링

### 기능별 입력 / 출력 데이터

| 기능 | 입력 데이터 | 출력 데이터 | 형식 |
| --- | --- | --- | --- |
| 화면 스왑 | 현재 EO/IR 배치 상태 | 큰 화면/작은 화면 배치 변경 | `bool`, `enum` |
| 모드 변경 | 운용 모드 값 | 현재 모드 상태 | `string`, `enum` |
| 탐지 변경 | 탐지 프로필 값 | 현재 탐지 프로필 상태 | `string`, `enum` |
| On / Off | 전원 토글 요청 | 시스템 운용 상태 | `bool` |
| 전자 zoom | 줌 on/off, 배율 증감 | 좌측 큰 화면 표시 배율 | `bool`, `double` |
| motor | 모터 on/off 요청 | 모터 상태 | `bool` |
| tracking | 추적 on/off 요청 | 추적 상태 | `bool` |
| 밝기 / 대조비 | 슬라이더 값 | 렌더러 표시 파라미터 | `double (0~100)` |
| 시스템 로그 | 버튼 이벤트, 장비 상태, 카메라 상태 | 시간순 로그 목록 | `string`, `JSON` |
| VLM 결과 출력 | 탐지/추적 이벤트 묶음 | 자연어 요약 결과 | `JSON`, `string` |

### 비행체 오버레이 표시 계획

- EO / IR 화면 위에 채움 없는 빨간색 사각형 박스를 그림
- 사각형 바로 옆에 빨간 텍스트로 탐지된 객체 이름을 표기
- 현재 GUI에는 이 구조를 위한 오버레이 레이어가 포함되어 있으며,
  추후 detector / tracker 결과를 연결하면 실시간으로 갱신 가능

### 권장 메시지 형식

#### 1. 카메라 프레임 메타데이터

```json
{
  "camera_id": "EO-01",
  "timestamp": "2026-04-03T10:05:00+09:00",
  "frame_width": 1920,
  "frame_height": 1080,
  "mode": "EO"
}
```

#### 2. 탐지 / 추적 결과

```json
{
  "track_id": "FW-01",
  "class": "fixed_wing_uav",
  "confidence": 0.94,
  "bbox": {
    "x": 620,
    "y": 152,
    "width": 140,
    "height": 82
  },
  "velocity": {
    "dx": -12.3,
    "dy": 4.8
  },
  "threat_level": "high"
}
```

#### 3. VLM / LLM 요약 응답

```json
{
  "timestamp": "2026-04-03T10:05:08+09:00",
  "summary": "북동측에서 고정익 1기와 쿼드콥터 3기가 식별되었습니다.",
  "route_analysis": "FW-01은 북동측에서 남서측으로 진입 중입니다.",
  "recommended_action": "FW-01을 우선 추적 대상으로 유지하십시오."
}
```

### 전자 zoom 처리 위치

- 현재 선택: GUI 렌더 레이어
- 이유:
  - 원본 영상 스트림은 유지
  - 카메라 PTZ 제어와 분리 가능
  - 좌측 큰 화면에만 선택적으로 적용 가능

## English

### Layout

- Top: dual EO / IR video area
- Center swap button: switches which feed is shown in the large viewport
- EO pane: currently uses the laptop webcam for GUI verification
- Bottom left: VLM result output
- Bottom center: realtime system log
- Bottom right: function controls and status panel

### Control Features

1. Mode
   - Airframe fixed
   - Manual
   - Tracking
2. Detection
   - Drone detection
   - Fixed-wing detection
   - Person detection
   - Composite detection
3. On / Off
4. Zoom
   - Electronic zoom changes only the large left viewport scale
   - It is separate from optical PTZ control
5. Motor
6. Tracking
7. Brightness / Contrast

### Data Flow

1. Camera ingest
2. Detector / tracker
3. Event buffer
4. VLM or LLM summary generation
5. WPF GUI rendering

### Input / Output Data By Feature

| Feature | Input Data | Output Data | Format |
| --- | --- | --- | --- |
| View swap | Current EO/IR placement | Large/small viewport reassignment | `bool`, `enum` |
| Mode change | Operation mode value | Current mode state | `string`, `enum` |
| Detection change | Detection profile value | Current detection profile state | `string`, `enum` |
| On / Off | Power toggle request | System state | `bool` |
| Electronic zoom | Zoom on/off, scale delta | Left large viewport display scale | `bool`, `double` |
| Motor | Motor toggle request | Motor state | `bool` |
| Tracking | Tracking toggle request | Tracking state | `bool` |
| Brightness / Contrast | Slider value | Renderer display parameter | `double (0~100)` |
| System log | Button events, device status, camera status | Time-ordered log list | `string`, `JSON` |
| VLM result output | Detection/tracking event bundle | Natural-language summary | `JSON`, `string` |

### Aircraft Overlay Plan

- Draw unfilled red bounding boxes on EO / IR video panes
- Show the recognized object name as red text right next to each box
- The current GUI already contains overlay layers for this future detector / tracker integration

### Recommended Message Formats

#### 1. Camera frame metadata

```json
{
  "camera_id": "EO-01",
  "timestamp": "2026-04-03T10:05:00+09:00",
  "frame_width": 1920,
  "frame_height": 1080,
  "mode": "EO"
}
```

#### 2. Detection / tracking result

```json
{
  "track_id": "FW-01",
  "class": "fixed_wing_uav",
  "confidence": 0.94,
  "bbox": {
    "x": 620,
    "y": 152,
    "width": 140,
    "height": 82
  },
  "velocity": {
    "dx": -12.3,
    "dy": 4.8
  },
  "threat_level": "high"
}
```

#### 3. VLM / LLM summary response

```json
{
  "timestamp": "2026-04-03T10:05:08+09:00",
  "summary": "One fixed-wing object and three quadcopters were identified in the north-east sector.",
  "route_analysis": "FW-01 is moving from north-east to south-west.",
  "recommended_action": "Keep FW-01 as the primary tracked object."
}
```

### Electronic Zoom Processing Layer

- Current choice: GUI render layer
- Why:
  - keeps the source stream untouched
  - separates display zoom from PTZ control
  - can be applied only to the large left viewport
