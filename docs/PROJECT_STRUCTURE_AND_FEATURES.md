# Project Structure And Features

This document is the current short map of the repository after removing
obsolete MEVA demo-video and laptop-webcam experiment paths.

## Active Runtime

```text
Jetson/Zybo camera and YOLO runtime
-> UDP video packets on 5000/5001
-> UDP DETS/STAT packets
-> WPF GUI rendering, recording, motor control, mobile alert page
```

## Active Folders

| Path | Role |
| --- | --- |
| `BroadcastControl.App` | Main WPF application for EO/IR monitoring and operator controls. |
| `BroadcastControl.App/Ros/ros_topic_gui_bridge.py` | Optional GUI-side bridge when the notebook itself reads ROS2 topics and forwards them to local GUI UDP ports. |
| `JetsonThor.RosCameraBridge` | Jetson-side Docker bridge that reads ROS2 camera topics and sends GUI-compatible UDP video packets. |
| `JetsonThor.EoTopicYoloDocker` | EO-topic YOLO bridge retained only for fallback or standalone YOLO bridge testing. It is not needed when `sentinel_bringup video_and_yolo.launch.py` already provides the YOLO output used by the GUI. |

## Removed Folders

| Path | Reason |
| --- | --- |
| `JetsonThor.MevaVideoDemoDocker` | MEVA demo videos are no longer used as the JetsonThor YOLO input path. |
| `JetsonThor.WebcamTopicYoloDocker` | Laptop webcam YOLO topic and UDP experiments are no longer used. |
| `LaptopWebcam.RosTopicPublisher` | Notebook webcam publishing is no longer part of the active camera path. |

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
| EO video and EO detections | `5000` |
| IR video and IR detections | `5001` |
| Mobile alert web app | `8088` |

## Notes

When another Docker runtime already launches the camera and YOLO pipeline with:

```bash
source /opt/ros/jazzy/setup.bash
source /ros2_ws/install/setup.bash
ros2 launch sentinel_bringup video_and_yolo.launch.py
```

the repository should avoid duplicating YOLO inference paths. Keep only the
bridge code that is needed to convert that runtime's output into the GUI UDP
format.
