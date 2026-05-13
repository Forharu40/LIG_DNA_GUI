# Project Structure And Features

This document is the current short map of the repository after removing
obsolete MEVA demo-video, laptop-webcam, and standalone EO YOLO experiment paths.

## Active Runtime

```text
Zybo / camera
-> Jetson video_rx, preprocessing, and YOLO ROS2 runtime
-> gui_camera_bridge subscribes ROS2 image and detection topics
-> SNTL UDP video and detection packets on 6000/6001
-> VLM result packets on 6002
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
| UDP VLM result receive | `BroadcastControl.App/Services/UdpVlmResultReceiverService.cs` |
| Viewport recording | `BroadcastControl.App/Services/ViewportRecordingService.cs` |
| Motor command and status | `BroadcastControl.App/Services/UdpMotorControlService.cs`, `BroadcastControl.App/Services/UdpMotorStatusReceiverService.cs` |
| Mobile alert web app | `BroadcastControl.App/Services/MobileAlertHubService.cs` |

## Packet Ports

| Data | Port |
| --- | --- |
| EO video and EO detections | `6000` |
| IR video | `6001` |
| VLM result | `6002` |
| Motor command | `8000` |
| Motor status | `8001` |
| Mobile alert web app | `8088` |

## Notes

The GUI system does not run YOLO inference by itself. YOLO runs in the Jetson
ROS2 runtime, and the bridge only forwards the already-published
`/tracks/eo` and `/tracks/ir` topics to the GUI.

IR camera input from Zybo to Jetson can still use `5001/udp`; that is a
different network segment from the Jetson bridge to PC GUI output port
`6001/udp`.

Motor data uses the `8000/8001` command/status split.
VLM results are separated onto `6002/udp` so image receive, motor feedback, and
analysis updates can be handled independently.

Motor packet details:

| Direction | Port | Size | Fields |
| --- | --- | --- | --- |
| GUI -> Thor | `8000/udp` | `9B` | mode, tracking, btn_mask, pan_pos, tilt_pos, scan_step, manual_step |
| Thor -> GUI | `8001/udp` | `36B` | pan motor 18B + tilt motor 18B |

GUI -> Thor uses little-endian integers. `pan_pos` and `tilt_pos` are UInt16
Dynamixel position values in the `0~4095` range. `btn_mask` uses `0x01` PAN+,
`0x02` PAN-, `0x04` TILT+, and `0x08` TILT-.
