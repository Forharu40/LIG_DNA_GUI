# Project Structure And Features

This document is the current short map of the repository after removing
obsolete MEVA demo-video, laptop-webcam, and standalone EO YOLO experiment paths.

## Active Runtime

```text
Zybo / camera
-> Jetson video_rx, preprocessing, and YOLO ROS2 runtime
-> gui_camera_bridge subscribes ROS2 image and detection topics
-> UDP video, DETS, and STAT packets on 6000/6001
-> WPF GUI rendering, recording, motor control, mobile alert page
```

## Active Folders

| Path | Role |
| --- | --- |
| `BroadcastControl.App` | Main WPF application for EO/IR monitoring and operator controls. |
| `JetsonThor.RosCameraBridge` | Jetson-side Docker bridge that reads ROS2 camera topics and sends GUI-compatible UDP video packets. |
| `BroadcastControl.UdpBenchmark` | UDP receive benchmark for EO/IR video and future YOLO/VLM/motor data transport checks. |
| `docs` | Project structure and feature notes. |

## Removed Folders

| Path | Reason |
| --- | --- |
| `JetsonThor.MevaVideoDemoDocker` | MEVA demo videos are no longer used as the JetsonThor YOLO input path. |
| `JetsonThor.WebcamTopicYoloDocker` | Laptop webcam YOLO topic and UDP experiments are no longer used. |
| `LaptopWebcam.RosTopicPublisher` | Notebook webcam publishing is no longer part of the active camera path. |
| `JetsonThor.EoTopicYoloDocker` | Standalone EO YOLO Docker is no longer used because YOLO detections are consumed from Jetson ROS2 topics. |

## Current GUI Responsibilities

| Area | Files |
| --- | --- |
| Layout and user interaction | `BroadcastControl.App/MainWindow.xaml`, `BroadcastControl.App/MainWindow.xaml.cs` |
| GUI state, mode, logs, recording status | `BroadcastControl.App/ViewModels/MainViewModel.cs` |
| UDP video, detection, and status packet receive | `BroadcastControl.App/Services/UdpEncodedVideoReceiverService.cs` |
| Viewport recording | `BroadcastControl.App/Services/ViewportRecordingService.cs` |
| Motor command and status | `BroadcastControl.App/Services/UdpMotorControlService.cs`, `BroadcastControl.App/Services/UdpMotorStatusReceiverService.cs` |
| Mobile alert web app | `BroadcastControl.App/Services/MobileAlertHubService.cs` |

## Packet Ports

| Data | Port |
| --- | --- |
| EO video and EO detections | `6000` |
| IR video and IR detections | `6001` |
| Mobile alert web app | `8088` |

## Notes

The GUI system does not run YOLO inference by itself. YOLO runs in the Jetson
ROS2 runtime, and the bridge only forwards the already-published
`/detections/eo` and `/detections/ir` topics to the GUI.

IR camera input from Zybo to Jetson can still use `5001/udp`; that is a
different network segment from the Jetson bridge to PC GUI output port
`6001/udp`.
