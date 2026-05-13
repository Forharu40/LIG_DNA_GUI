# LIG DNA GUI

운용통제 GUI와 Jetson ROS2 브릿지를 함께 사용하는 EO/IR 감시 시스템입니다.

PC의 WPF GUI는 Jetson ROS2 토픽을 직접 구독하지 않고, Jetson에서 실행되는
`gui_camera_bridge`가 ROS2 토픽을 구독한 뒤 GUI 전용 UDP 패킷으로 변환해 보내는
구조를 기본으로 사용합니다.

## 전체 시스템 흐름

```text
Zybo / Camera
  -> Jetson video_rx node
  -> /camera/eo, /camera/ir
  -> EO/IR preprocessing node
  -> /video/eo/preprocessed
  -> YOLO detector node
  -> /detections/eo, /detections/ir
  -> gui_camera_bridge
  -> PC GUI UDP 6000/6001
  -> BroadcastControl.App 화면 출력, 바운딩 박스, 녹화, 알림, 모터 제어
```

## 주요 구성 요소

| 구성 | 역할 |
| --- | --- |
| `BroadcastControl.App` | Windows WPF 운용통제 GUI |
| `JetsonThor.RosCameraBridge` | Jetson ROS2 토픽을 PC GUI UDP 패킷으로 변환하는 Docker 브릿지 |
| `BroadcastControl.UdpBenchmark` | EO/IR UDP 수신 성능 비교 도구 |
| `docs` | 구조와 기능 정리 문서 |

## PC GUI 역할

`BroadcastControl.App`는 PC에서 실행되는 운용통제 화면입니다.

- EO 영상 UDP 수신: `6000`
- IR 영상 UDP 수신: `6001`
- YOLO detection 패킷 수신 및 바운딩 박스 표시
- EO/IR 화면 회전, 전자 줌, 밝기/대조비 조절
- 녹화 영상 목록 확인 및 재생
- 위험 등급, 주 탐지체, VLM 분석 결과 표시
- 모바일 위험 알림 웹앱 제공
- 모터 수동 제어, 각도 설정, 모터 상태 표시

GUI는 Jetson ROS2 토픽을 직접 받는 것이 아니라, Jetson bridge가 보내는 UDP 패킷을 받습니다.

## Jetson Bridge 역할

`gui_camera_bridge`는 Jetson에서 실행되는 Docker 컨테이너입니다.

구독하는 ROS2 토픽:

| 데이터 | 기본 토픽 |
| --- | --- |
| EO image | `/video/eo/preprocessed` |
| IR image | `/camera/ir` |
| EO detection | `/detections/eo` |
| IR detection | `/detections/ir` |

PC GUI로 보내는 UDP 포트:

| 데이터 | GUI 포트 |
| --- | --- |
| EO 영상 및 EO detection | `6000` |
| IR 영상 및 IR detection | `6001` |

IR 카메라가 Zybo에서 Jetson `video_rx_node`로 들어올 때는 `5001` 포트를 사용합니다.
혼동을 피하기 위해 Jetson bridge에서 PC GUI로 보내는 IR 포트는 `6001`로 분리했습니다.

## 실행 순서

### 1. Jetson에서 ROS2 영상 토픽 확인

```bash
docker exec thor2 bash -lc 'source /opt/ros/jazzy/setup.bash; source /ros2_ws/install/local_setup.bash; ros2 topic list | grep -E "camera|video|detection"'
docker exec thor2 bash -lc 'source /opt/ros/jazzy/setup.bash; source /ros2_ws/install/local_setup.bash; timeout 10 ros2 topic hz /camera/ir'
```

### 2. Jetson에서 bridge 실행

```bash
cd ~/LIG_DNA_GUI/JetsonThor.RosCameraBridge
GUI_HOST=192.168.1.94 bash ./run_camera_udp_bridge.sh
```

이미지를 새로 빌드해야 할 때:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.RosCameraBridge
GUI_HOST=192.168.1.94 bash ./run_camera_udp_bridge.sh --build
```

`GUI_HOST`는 PC 노트북의 IPv4 주소입니다.

### 3. PC에서 GUI 실행

Visual Studio에서 `BroadcastControl.App`를 시작 프로젝트로 설정한 뒤 실행합니다.

## FastDDS no-shm 설정

Jetson에서 `thor2` 컨테이너는 `/camera/ir` 토픽을 정상 수신하지만,
별도 `gui_camera_bridge` 컨테이너가 이미지 메시지를 받지 못하는 경우가 있었습니다.

원인은 컨테이너 간 DDS shared memory 전송 문제였고, 현재
`run_camera_udp_bridge.sh`는 기본적으로 FastDDS shared memory를 끄는 설정을 자동 적용합니다.

기본값:

```text
FASTDDS_NO_SHM=true
```

스크립트가 자동으로 생성하는 파일:

```text
JetsonThor.RosCameraBridge/fastdds_no_shm.xml
```

기본 설정을 끄고 원래 방식으로 실행하려면:

```bash
FASTDDS_NO_SHM=false GUI_HOST=192.168.1.94 bash ./run_camera_udp_bridge.sh
```

## 문제 확인 명령어

### Zybo -> Jetson IR 입력 확인

```bash
sudo tcpdump -ni any udp port 5001
```

### Jetson video_rx 상태 확인

```bash
docker exec thor2 bash -lc 'source /opt/ros/jazzy/setup.bash; source /ros2_ws/install/local_setup.bash; ros2 topic echo /camera/ir/rx_status --once'
```

### Bridge 로그 확인

```bash
docker logs --tail 100 gui_camera_bridge
```

정상 로그 예:

```text
Streaming IR UDP packets to 192.168.1.94:6001
IR first image sent!
```

### Jetson -> PC GUI UDP 송신 확인

```bash
sudo tcpdump -ni any dst host 192.168.1.94 and udp and \( port 6000 or port 6001 \)
```

### Windows에서 UDP 수신 확인

GUI를 끈 뒤 PowerShell에서:

```powershell
$udp = New-Object System.Net.Sockets.UdpClient(6001)
$ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
while ($true) {
  $bytes = $udp.Receive([ref]$ep)
  Write-Host "$($ep.Address):$($ep.Port) length=$($bytes.Length)"
}
```

## PC GUI 빌드

```powershell
dotnet build .\BroadcastControl.App\BroadcastControl.App.csproj
```

현재 GUI는 `net10.0-windows`를 대상으로 하므로 Windows Desktop을 포함한 .NET 10 SDK가 필요합니다.
