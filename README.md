# LIG DNA GUI

2026-05-28 ver2 refactoring branch.

이 저장소는 Windows WPF 기반 EO/IR 감시 GUI와 Jetson ROS2 UDP 브릿지를 함께 관리합니다. GUI는 Jetson의 ROS2 토픽을 직접 구독하지 않고, Jetson 브릿지가 UDP/HTTP로 변환한 카메라 영상, 탐지 결과, VLM 결과, 모터 상태를 받아 화면에 표시합니다.

## 2026-05-28 ver2 리팩터링 요약

- `ViewModels`를 기능별 폴더로 나누기 위한 기본 구조를 추가했습니다.
- `Models` 폴더를 신설해 카메라, 모터, 네트워크, 모바일 알림, VLM 데이터 모델을 서비스 파일에서 분리했습니다.
- `Services`를 카메라, 모터, 네트워크, 녹화, 모니터링, 알림, VLM 영역으로 정리했습니다.
- `MainWindow.xaml`을 shell 구조로 축소하고 실제 화면을 기능별 UserControl로 분리했습니다.
- 모터 제어 패킷 직렬화를 `MotorPacketSerializer`로 분리하고, Jetson으로 보내는 모터 명령을 아래 10바이트 명세에 맞췄습니다.
- 카메라 영상은 실제 영상 비율을 유지하도록 `Uniform` 표시를 사용합니다. 비율이 맞지 않는 영역에는 검정 여백이 보일 수 있습니다.

## 전체 실행 구조

```text
Camera / Sensor
  -> Jetson ROS2 nodes
  -> JetsonThor.RosCameraBridge
  -> UDP / HTTP
  -> BroadcastControl.App
```

## 주요 폴더

| 경로 | 역할 |
| --- | --- |
| `BroadcastControl.App` | Windows WPF GUI 애플리케이션 |
| `BroadcastControl.App/Models` | UDP/HTTP/화면 상태에 쓰이는 순수 데이터 모델 |
| `BroadcastControl.App/Services` | UDP 송수신, 모터 명령, 녹화, 모바일 알림, VLM 결과 수신 등 외부 입출력 |
| `BroadcastControl.App/ViewModels` | 화면 상태와 명령을 기능별로 관리하는 MVVM 계층 |
| `BroadcastControl.App/Views` | 기능별 화면 UserControl |
| `JetsonThor.RosCameraBridge` | Jetson에서 ROS2 토픽을 UDP/HTTP로 변환하는 브릿지 |
| `BroadcastControl.UdpBenchmark` | UDP 수신 성능 확인용 도구 |
| `docs` | 보조 문서 |

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
      IMobileAlertService.cs
      MobileAlertService.cs
      MobileAlertHubService.cs
    Camera/
      ICameraFrameService.cs
      CameraFrameService.cs
      DetectionOverlayService.cs
      UdpEncodedVideoReceiverService.cs
    Monitoring/
      ISystemLogService.cs
      SystemLogService.cs
    Motor/
      IMotorCommandService.cs
      MotorCommandService.cs
      MotorPacketSerializer.cs
      UdpMotorControlService.cs
      UdpMotorStatusReceiverService.cs
    Network/
      IUdpReceiverService.cs
      IUdpSenderService.cs
      UdpReceiverService.cs
      UdpSenderService.cs
    Recording/
      IRecordingService.cs
      RecordingService.cs
      ViewportRecordingService.cs
    Vlm/
      IVlmService.cs
      VlmService.cs
      UdpVlmResultReceiverService.cs
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

## 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `App.xaml`, `App.xaml.cs` | WPF 앱 시작, 테마 적용, 전역 리소스 초기화 |
| `MainWindow.xaml` | 기능별 active view를 배치하는 shell 레이아웃 |
| `MainWindow.xaml.cs` | UDP 서비스 연결과 active view 이벤트 연결, 영상 렌더링, 탐지 오버레이, 녹화/모바일/VLM 이벤트 연결 |
| `Infrastructure/RelayCommand.cs` | ViewModel 명령을 WPF `ICommand`로 연결하는 공통 클래스 |
| `Models/Camera/VideoStreamModels.cs` | `ReceivedVideoFrame`, `DetectionInfo`, `DetectionPacket`, `YoloStatusPacket`, 녹화 segment 모델 |
| `Models/Motor/MotorControlModels.cs` | 방향키 버튼 비트마스크 `MotorButtonMask` |
| `Models/Motor/MotorStatusModels.cs` | Jetson에서 들어오는 pan/tilt 모터 상태 모델 |
| `Models/Network/AppNetworkSettings.cs` | `LigDnaGui.config.json` 로드/저장, Jetson IP, GUI IP, UDP/HTTP 포트 설정 |
| `Models/Mobile/MobileAlertModels.cs` | 모바일 웹앱으로 전달하는 알림 이벤트 모델 |
| `Models/Vlm/VlmResultModels.cs` | VLM 분석 결과, 객체별 threat level, 분석 메시지 모델 |
| `Services/Camera/UdpEncodedVideoReceiverService.cs` | EO/IR UDP 영상 조립, JPEG 디코딩, 탐지/status 패킷 파싱 |
| `Services/Camera/CameraFrameService.cs` | 카메라 프레임 수신 서비스를 인터페이스 뒤로 감싸는 어댑터 |
| `Services/Camera/DetectionOverlayService.cs` | 실제 카메라 비율 유지 시 오버레이 좌표 계산에 필요한 공통 유틸 |
| `Services/Motor/UdpMotorControlService.cs` | GUI에서 Jetson으로 모터 명령 UDP 전송 |
| `Services/Motor/MotorPacketSerializer.cs` | 모터 명령 10바이트 패킷 직렬화 |
| `Services/Motor/UdpMotorStatusReceiverService.cs` | Jetson에서 들어오는 모터 상태 UDP 패킷 수신/파싱 |
| `Services/Motor/MotorCommandService.cs` | 모터 명령 서비스를 인터페이스 뒤로 감싸는 어댑터 |
| `Services/Network/UdpReceiverService.cs` | 범용 UDP 수신 서비스 |
| `Services/Network/UdpSenderService.cs` | 범용 UDP 송신 서비스 |
| `Services/Recording/ViewportRecordingService.cs` | 현재 GUI 화면 영역을 로컬 영상 파일로 녹화 |
| `Services/Recording/RecordingService.cs` | 녹화 기능을 인터페이스 뒤로 감싸는 어댑터 |
| `Services/Alert/MobileAlertHubService.cs` | 모바일 웹앱용 HTTP/SSE 알림 서버 |
| `Services/Alert/MobileAlertService.cs` | 모바일 알림 기능을 인터페이스 뒤로 감싸는 어댑터 |
| `Services/Vlm/UdpVlmResultReceiverService.cs` | VLM 분석 결과 UDP 수신/파싱 |
| `Services/Vlm/VlmService.cs` | VLM 결과 수신 기능을 인터페이스 뒤로 감싸는 어댑터 |
| `Services/Monitoring/SystemLogService.cs` | 시스템 로그 이벤트를 분리하기 위한 서비스 |
| `ViewModels/MainViewModel.cs` | 기존 GUI 상태와 명령의 중심 ViewModel. 새 기능별 ViewModel을 포함하는 루트 역할도 수행 |
| `ViewModels/ViewModelBase.cs` | 기능별 ViewModel의 `INotifyPropertyChanged` 공통 베이스 |
| `ViewModels/Camera/CameraViewModel.cs` | EO/IR 프레임, 줌, 밝기, 대비 상태 |
| `ViewModels/Motor/MotorControlViewModel.cs` | 모터 각도, 목표 raw 위치, scan/manual step, 방향키 상태 |
| `ViewModels/Operation/OperationControlViewModel.cs` | scan/manual, tracking, 선택 track id, EO/IR 우선 상태 |
| `ViewModels/Monitoring/MonitoringViewModel.cs` | 시스템 로그, YOLO 대상 목록, 위협도, Jetson 연결 상태 |
| `ViewModels/Recording/RecordingViewModel.cs` | 녹화 상태와 녹화된 영상 목록 |
| `ViewModels/Vlm/VlmViewModel.cs` | VLM 최신 분석과 분석 히스토리 |
| `ViewModels/Mobile/MobileWebAppViewModel.cs` | 모바일 알림 서버 상태, 포트, 최신 evidence URL |
| `Views/Camera/CameraView.xaml` | 중앙 카메라 영상, 탐지 오버레이, 인셋 영상 화면 |
| `Views/Motor/MotorControlView.xaml` | 좌측 전원, 보조 카메라, 모터 위치/각도/속도/방향키 패널 |
| `Views/Operation/OperationControlView.xaml` | 하단 밝기/대비/줌/모드/트래킹/네트워크 상태 패널 |
| `Views/Monitoring/MonitoringView.xaml` | 우측 녹화 상태, YOLO Targets, System Log 패널 |
| `Views/Recording/RecordedVideosView.xaml` | 녹화 영상 목록과 재생 오버레이 |
| `Views/Motor/MotorDetailsView.xaml` | 모터 상세 상태 오버레이 |
| `Views/Settings/SettingsDrawerView.xaml` | 설정 drawer 오버레이 |

## 모터 제어 UDP 패킷

GUI는 Jetson의 `8000/udp`로 10바이트 little-endian 패킷을 보냅니다.

| 바이트 | 필드 | 설명 |
| --- | --- | --- |
| `0` | `mode` | `0=scan`, `1=manual` |
| `1` | `tracking` | `0=off`, `1=on` |
| `2` | `track_id` | `0~254=객체 id`, `0xff=auto` |
| `3` | `btn_mask` | 방향키 비트마스크 |
| `4~5` | `pan_pos` | pan raw 위치 `0~4095` |
| `6~7` | `tilt_pos` | tilt raw 위치 `0~4095` |
| `8` | `scan_step` | scan/auto step size |
| `9` | `manual_step` | manual step size |

`stream_select` 값은 이 모터 제어 패킷에 포함하지 않습니다. EO/IR 기준 정보가 필요한 추적 녹화 제어는 `8010/udp` 보조 패킷으로 별도 전송합니다.

## 네트워크 포트

| 포트 | 방향 | 기능 |
| --- | --- | --- |
| `6000/udp` | Jetson -> GUI | EO 영상 |
| `6001/udp` | Jetson -> GUI | IR 영상 |
| `6002/udp` | Jetson -> GUI | 탐지 결과 |
| `6003/udp` | Jetson/VLM -> GUI | VLM 분석 결과 |
| `8000/udp` | GUI -> Jetson | 모터 제어 명령 |
| `8001/udp` | Jetson -> GUI | 모터 상태 |
| `8010/udp` | GUI -> Jetson bridge | tracking 녹화 시작/종료 보조 제어 |
| `8088/tcp` | Mobile -> GUI | 모바일 위험 알림 HTTP/SSE |
| `8090/tcp` | GUI -> Jetson | 녹화 영상 목록/다운로드 HTTP |

## 빌드

```powershell
dotnet build BroadcastControl.slnx -c Debug --no-restore
```

현재 빌드는 성공하며, `App.xaml.cs`의 Windows 전용 API에 대한 기존 CA1416 경고가 남아 있습니다.
