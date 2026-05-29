# LIG DNA GUI

Windows WPF 기반 EO/IR 감시 GUI입니다. Jetson ROS2 시스템에서 UDP/HTTP로 전달되는 카메라 영상, YOLO 탐지 결과, VLM 분석 결과, 모터 상태, 녹화 영상 목록을 받아 화면에 표시하고, GUI에서 만든 모터/추적 명령을 Jetson으로 다시 전송합니다.

## 전체 흐름

```text
EO/IR Camera, YOLO, VLM, Motor
        |
        v
Jetson / ROS2 / gui_bridge
        |
        |  UDP 6000/6001: EO/IR JPEG 영상
        |  UDP 6002     : YOLO 탐지 결과
        |  UDP 6003     : VLM 분석 결과
        |  UDP 8001     : 모터 상태
        |  HTTP 8090    : 녹화 영상 목록/다운로드
        v
BroadcastControl.App
        |
        |  UDP 8000     : GUI 모터/추적 명령
        v
Jetson / ROS2 Motor Control
```

## MVVM 구조

```text
View
  CameraView, MotorControlView, RecordingView, OperationControlView,
  MonitoringView, SettingsDrawerView, VlmPanelView
        |
        | Binding / UI Event
        v
ViewModel
  MainViewModel
  CameraViewModel, MotorControlViewModel, RecordingViewModel,
  OperationControlViewModel, MonitoringViewModel, VlmViewModel,
  MobileWebAppViewModel
        |
        | Command / State Update
        v
Service
  UDP 수신/송신, 영상 디코딩, 모터 패킷, 녹화, VLM 수신, 모바일 알림
        |
        | Parse / Serialize
        v
Model
  영상 프레임, 탐지 객체, 모터 패킷, 네트워크 설정, VLM 결과, 녹화 영상 정보
```

### View

사용자가 보는 WPF 화면입니다. XAML은 화면 배치를 담당하고, `*.xaml.cs`는 마우스 클릭, 버튼 클릭, 재생 제어처럼 XAML 요소와 직접 연결되는 UI 이벤트만 처리합니다.

### ViewModel

화면에 표시할 상태와 버튼 명령을 관리합니다. 기능별 ViewModel 폴더 안에 해당 기능의 상태와 `MainViewModel` partial 코드를 함께 두어, 기존 XAML 바인딩을 유지하면서 기능별 책임을 분리했습니다.

### Model

UDP 패킷, 영상 프레임, 탐지 객체, 모터 상태, VLM 결과, 설정 JSON처럼 데이터 구조를 정의합니다.

### Service

실제 기능 수행 계층입니다. UDP/HTTP 통신, JPEG 프레임 재조립, 모터 명령 직렬화, 녹화, 모바일 알림 서버 같은 작업을 담당합니다.

## 주요 폴더

| 경로 | 역할 |
| --- | --- |
| `BroadcastControl.App` | Windows WPF GUI 애플리케이션 |
| `BroadcastControl.App/Views` | 기능별 화면 UserControl |
| `BroadcastControl.App/ViewModels` | 화면 상태와 명령을 관리하는 MVVM 계층 |
| `BroadcastControl.App/Models` | UDP/HTTP/화면 상태 데이터 모델 |
| `BroadcastControl.App/Services` | 통신, 영상, 모터, 녹화, VLM, 알림 처리 |
| `BroadcastControl.App/Infrastructure` | 공통 WPF 인프라 코드 |
| `JetsonThor.RosCameraBridge` | Jetson ROS2 토픽을 UDP/HTTP로 변환하는 브리지 |
| `BroadcastControl.UdpBenchmark` | UDP 수신 성능 확인 도구 |

## BroadcastControl.App 구조

```text
BroadcastControl.App/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  LigDnaGui.config.json

  Infrastructure/
    RelayCommand.cs

  Models/
    Camera/
      VideoStreamModels.cs
    Mobile/
      MobileAlertModels.cs
    Motor/
      MotorControlModels.cs
      MotorStatusModels.cs
    Network/
      AppNetworkSettings.cs
    Vlm/
      VlmResultModels.cs

  Services/
    Alert/
    Camera/
    Monitoring/
    Motor/
    Network/
    Recording/
    Vlm/

  ViewModels/
    MainViewModel.cs
    ViewModelBase.cs
    Camera/
      CameraViewModel.cs
    Mobile/
      MobileWebAppViewModel.cs
    Monitoring/
      MonitoringViewModel.cs
    Motor/
      MotorControlViewModel.cs
    Operation/
      OperationControlViewModel.cs
    Recording/
      RecordingViewModel.cs
    Vlm/
      VlmViewModel.cs

  Views/
    Camera/
      CameraView.xaml
      CameraView.xaml.cs
    Monitoring/
      MonitoringView.xaml
      MonitoringView.xaml.cs
    Motor/
      MotorControlView.xaml
      MotorControlView.xaml.cs
      MotorDetailsView.xaml
      MotorDetailsView.xaml.cs
    Operation/
      OperationControlView.xaml
      OperationControlView.xaml.cs
    Recording/
      RecordedVideosView.xaml
      RecordedVideosView.xaml.cs
    Settings/
      SettingsDrawerView.xaml
      SettingsDrawerView.xaml.cs
    Vlm/
      VlmPanelView.xaml
      VlmPanelView.xaml.cs
```

## ViewModel 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `ViewModels/MainViewModel.cs` | 루트 ViewModel입니다. 공통 필드, 생성자, Command 선언, 공통 이벤트, 보조 타입을 관리합니다. |
| `ViewModels/ViewModelBase.cs` | 모든 ViewModel이 상속하는 기반 클래스입니다. `INotifyPropertyChanged`와 `SetProperty`를 제공해 WPF Binding 갱신을 공통 처리합니다. |
| `ViewModels/Camera/CameraViewModel.cs` | EO/IR 프레임, 큰 화면/작은 화면 전환, 줌, 팬, 회전, 밝기/대비 등 카메라 화면 상태를 관리합니다. |
| `ViewModels/Motor/MotorControlViewModel.cs` | 모터 Pan/Tilt, 방향키, 각도 입력, Motor Speed, 모터 명령 패킷 송신 상태를 관리합니다. |
| `ViewModels/Operation/OperationControlViewModel.cs` | 전원, Scan/Manual 모드, Tracking, 테마, 언어, 주 탐지체, System 연결 표시를 관리합니다. |
| `ViewModels/Recording/RecordingViewModel.cs` | 녹화 상태, 수동 녹화, 녹화 영상 목록, 분석/시스템 로그 저장을 관리합니다. |
| `ViewModels/Vlm/VlmViewModel.cs` | YOLO 탐지 객체, VLM 분석 결과, 위험도, 추적 객체 ID 선택을 관리합니다. |
| `ViewModels/Monitoring/MonitoringViewModel.cs` | 시스템 로그, YOLO 타겟 요약, 위험도와 연결 상태를 모니터링 화면용으로 보관합니다. |
| `ViewModels/Mobile/MobileWebAppViewModel.cs` | 모바일 위험 알림 서버 상태, 포트, 최신 증거 이미지 URL을 관리합니다. |

## View 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `Views/Camera/CameraView.xaml` | EO/IR 영상, 바운딩 박스, 미니맵, 녹화/연결 상태를 표시합니다. |
| `Views/Camera/CameraView.xaml.cs` | 영상 클릭, 객체 선택, 줌/회전처럼 카메라 화면과 직접 연결된 UI 이벤트를 처리합니다. |
| `Views/Motor/MotorControlView.xaml` | 모터 위치, 각도 입력, Motor Speed, 방향키 UI를 표시합니다. |
| `Views/Motor/MotorControlView.xaml.cs` | 모터 방향키, 각도 입력, 반복 전송, 모터 상세 버튼 이벤트를 처리합니다. |
| `Views/Motor/MotorDetailsView.xaml` | 모터 상세 상태 오버레이를 표시합니다. |
| `Views/Motor/MotorDetailsView.xaml.cs` | 모터 상세 오버레이의 배경 클릭 이벤트를 전달합니다. |
| `Views/Operation/OperationControlView.xaml` | 하단 조작 패널, 네트워크 설정 입력, 모드 버튼을 표시합니다. |
| `Views/Operation/OperationControlView.xaml.cs` | 모드 전환과 네트워크 설정 저장 UI 이벤트를 연결합니다. |
| `Views/Monitoring/MonitoringView.xaml` | Recording/System 상태, YOLO 타겟 리스트, 시스템 로그를 표시합니다. |
| `Views/Monitoring/MonitoringView.xaml.cs` | 모니터링 화면 버튼과 상태 표시 이벤트를 연결합니다. |
| `Views/Recording/RecordedVideosView.xaml` | 녹화 영상 목록과 재생 오버레이를 표시합니다. |
| `Views/Recording/RecordedVideosView.xaml.cs` | 녹화 목록 조회, 폴더 이동, 재생, 확대/이동 이벤트를 처리합니다. |
| `Views/Settings/SettingsDrawerView.xaml` | 주 탐지체, 테마, 언어, 화면 모드, 네트워크 설정 drawer를 표시합니다. |
| `Views/Settings/SettingsDrawerView.xaml.cs` | 설정 drawer 배경 클릭과 창 모드 전환 이벤트를 처리합니다. |
| `Views/Vlm/VlmPanelView.xaml` | VLM/YOLO 분석 패널을 표시합니다. |
| `Views/Vlm/VlmPanelView.xaml.cs` | VLM 패널 XAML 초기화만 담당합니다. |

## Model 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `Models/Camera/VideoStreamModels.cs` | UDP 영상 프레임, 탐지 결과, YOLO 상태, 녹화 segment 관련 데이터 구조입니다. |
| `Models/Motor/MotorControlModels.cs` | 모터 방향키 비트마스크와 제어 패킷 관련 데이터 구조입니다. |
| `Models/Motor/MotorStatusModels.cs` | Jetson에서 들어오는 Pan/Tilt 모터 상태 구조입니다. |
| `Models/Network/AppNetworkSettings.cs` | `LigDnaGui.config.json` 로드/저장, Jetson IP, GUI IP, 포트 설정을 관리합니다. |
| `Models/Mobile/MobileAlertModels.cs` | 모바일 위험 알림 이벤트 데이터 구조입니다. |
| `Models/Vlm/VlmResultModels.cs` | VLM 분석 결과, 객체별 위험도, 분석 메시지 데이터 구조입니다. |

## Service 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `Services/Camera/UdpEncodedVideoReceiverService.cs` | EO/IR JPEG UDP 프레임을 수신, 재조립, 디코딩하고 탐지/status 패킷을 파싱합니다. |
| `Services/Camera/DetectionOverlayService.cs` | 영상 비율 기준으로 바운딩 박스 오버레이 좌표를 계산합니다. |
| `Services/Motor/UdpMotorControlService.cs` | GUI에서 Jetson으로 모터 제어 UDP 패킷을 전송합니다. |
| `Services/Motor/MotorPacketSerializer.cs` | 모터 명령 패킷을 Jetson 규격에 맞게 직렬화합니다. |
| `Services/Motor/UdpMotorStatusReceiverService.cs` | Jetson 모터 상태 UDP 패킷을 수신하고 파싱합니다. |
| `Services/Recording/ViewportRecordingService.cs` | GUI 화면 영역을 로컬 영상 파일로 녹화합니다. |
| `Services/Alert/MobileAlertHubService.cs` | 모바일 위험 알림용 HTTP/SSE 서버를 제공합니다. |
| `Services/Vlm/UdpVlmResultReceiverService.cs` | VLM 분석 결과 UDP 패킷을 수신하고 파싱합니다. |
| `Services/Network/UdpReceiverService.cs` | 범용 UDP 수신 기능을 제공합니다. |
| `Services/Network/UdpSenderService.cs` | 범용 UDP 송신 기능을 제공합니다. |
| `Services/Monitoring/SystemLogService.cs` | 시스템 로그 이벤트를 분리해 다루기 위한 서비스입니다. |

## 통신 포트

| 포트 | 방향 | 기능 |
| --- | --- | --- |
| `6000/udp` | Jetson -> GUI | EO JPEG 영상 프레임 |
| `6001/udp` | Jetson -> GUI | IR JPEG 영상 프레임 |
| `6002/udp` | Jetson -> GUI | EO/IR YOLO 탐지 결과 |
| `6003/udp` | Jetson/VLM -> GUI | VLM 분석 결과 |
| `8000/udp` | GUI -> Jetson | 모터/추적 명령 |
| `8001/udp` | Jetson -> GUI | 모터 상태 |
| `8090/http` | Jetson -> GUI | 녹화 영상 목록/다운로드 |

## 영상 수신 형식

EO/IR 영상은 raw image가 아니라 JPEG로 압축된 프레임입니다. Jetson 브리지가 각 프레임을 JPEG로 인코딩한 뒤 UDP 청크로 나누어 보내고, GUI가 같은 frame id의 청크를 모아 JPEG를 복원한 다음 WPF 화면에 표시합니다.

```text
ROS Image
  -> JPEG 압축
  -> UDP 청크 분할
  -> GUI UDP 수신
  -> JPEG 재조립/디코딩
  -> View 표시
```

## 모터 명령 패킷

GUI는 Jetson의 `8000/udp`로 11바이트 little-endian 명령 패킷을 보냅니다.

| Byte | 필드 | 설명 |
| --- | --- | --- |
| `0` | `mode` | `0=scan`, `1=manual` |
| `1` | `tracking` | `0=off`, `1=on` |
| `2` | `track_id` | `0~254=object id`, `0xff=auto` |
| `3` | `stream_select` | `0=EO`, `1=IR` |
| `4` | `btn_mask` | 방향키 비트마스크 |
| `5~6` | `pan_pos` | pan raw 위치 `0~4095` |
| `7~8` | `tilt_pos` | tilt raw 위치 `0~4095` |
| `9` | `scan_step` | scan 모드 모터 step |
| `10` | `manual_step` | manual 모드 모터 step |

각도 입력은 GUI에서 degree로 표시하지만, 전송 시에는 다음 식으로 raw step으로 변환합니다.

```text
raw = deg / 360.0 * 4096.0
```

최종 전송값은 `0~4095` 범위로 제한합니다.

## 빌드

```powershell
dotnet build BroadcastControl.slnx -c Debug
```

네트워크 복원이 이미 끝난 상태에서는 다음 명령으로 빠르게 확인할 수 있습니다.

```powershell
dotnet build BroadcastControl.slnx -c Debug --no-restore
```
