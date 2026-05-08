# LIG DNA GUI

WPF based EO/IR monitoring GUI for receiving Jetson/Zybo video streams,
displaying YOLO detection results, controlling motors, recording viewports,
and serving mobile risk alerts.

## Current Runtime Flow

```text
Zybo/Jetson cameras
-> Jetson ROS2 and YOLO runtime
-> GUI UDP video and detection packets
-> BroadcastControl.App
-> EO/IR display, overlays, recording, motor control, mobile alert page
```

The GUI receives JPEG UDP video frames and detection packets. EO video uses
port `5000`, IR video uses port `5001`, and the mobile alert app is served on
port `8088`.

## Active Folders

| Folder | Purpose |
| --- | --- |
| `BroadcastControl.App` | Main Windows WPF GUI application. |
| `BroadcastControl.App/Ros` | Optional local ROS2-to-GUI UDP bridge script copied with the app. |
| `JetsonThor.RosCameraBridge` | Jetson-side Docker bridge for ROS2 EO/IR camera topics to GUI UDP packets. |
| `JetsonThor.EoTopicYoloDocker` | Legacy EO-topic YOLO bridge kept only if a separate YOLO bridge is still needed. Prefer the integrated `sentinel_bringup` launch when it already provides YOLO output. |
| `docs` | Current folder and feature summary. |

## Removed Legacy Paths

The old MEVA demo-video YOLO path and laptop-webcam experiment path were
removed because they are no longer part of the active workflow:

| Removed path | Reason |
| --- | --- |
| `JetsonThor.MevaVideoDemoDocker` | MEVA demo video is no longer used as a YOLO source on JetsonThor. |
| `JetsonThor.WebcamTopicYoloDocker` | Laptop webcam topic/UDP YOLO experiments are no longer used. |
| `LaptopWebcam.RosTopicPublisher` | Laptop webcam publishing is no longer part of the runtime path. |
| Old MEVA/Webcam docs under `docs` | Superseded by the current folder and feature summary. |

## Main Features

| Feature | Location |
| --- | --- |
| EO/IR UDP video receive and decode | `BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs` |
| Detection packet receive and overlay rendering | `BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs`, `BroadcastControl.App/MainWindow.xaml.cs` |
| Viewport recording and recorded video browsing | `BroadcastControl.App/Services/ViewportRecordingService.cs`, `BroadcastControl.App/MainWindow.xaml.cs` |
| Motor control and motor status receive | `BroadcastControl.App/Services/UdpMotorControlService.cs`, `BroadcastControl.App/Services/UdpMotorStatusReceiverService.cs` |
| Mobile risk alert web app | `BroadcastControl.App/Services/MobileAlertHubService.cs` |
| GUI state and commands | `BroadcastControl.App/ViewModels/MainViewModel.cs` |

## Build

```powershell
dotnet build .\BroadcastControl.App\BroadcastControl.App.csproj
```

The app currently targets `net10.0-windows`, so a .NET 10 SDK with Windows
Desktop support is required.
