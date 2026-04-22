# Live Camera Low-Latency Stream Guide

This document describes the new live camera streaming path added on top of the existing MEVA demo path.

MEVA stays available. Nothing is removed.

## 1. Goal

The MEVA sender was useful for packet-format and GUI integration testing, but a live camera path needs a simpler and lower-latency structure.

This branch adds a dedicated live camera sender that:

- keeps the existing GUI UDP packet format
- keeps MEVA streaming untouched
- uses the same 4-lane execution idea
- removes file playback and timeline logic
- focuses on latest-frame delivery instead of demo playback

## 2. Added files

- `JetsonThor.MevaYoloDocker/app/stream_live_camera_yolo.py`
- `JetsonThor.MevaYoloDocker/run_live_camera_yolo.sh`

The existing files remain valid:

- `JetsonThor.MevaYoloDocker/app/stream_meva_yolo.py`
- `JetsonThor.MevaYoloDocker/run_meva_yolo_demo.sh`

## 3. Architecture

The new live camera sender is designed for EO live input on Jetson.

```text
Camera source
-> Main capture loop
-> YOLO worker
-> JPEG encode worker
-> UDP send worker
-> GUI EO receiver
```

### 3-1. Why this is lower latency than MEVA

MEVA includes extra work that a real camera path does not need:

- file open / clip sampling
- clip start / duration / interval logic
- playback pacing for demo video
- MEVA metadata packets for sampled segments

The live camera path removes those steps and instead does:

- read latest camera frame
- run YOLO on a copy when the detection timer says it is time
- compress a GUI stream frame
- send only the latest available frame

That means the sender is closer to:

```text
capture -> detect -> encode -> send
```

instead of:

```text
find clip -> seek file -> pace playback -> detect -> encode -> send
```

## 4. Execution lanes

The live camera sender keeps the same idea of 4 execution lanes:

1. Main loop
   - reads the camera
   - prepares GUI stream frames
   - decides when to trigger YOLO
   - avoids waiting on heavy work

2. YOLO detection worker
   - runs inference on the source frame
   - updates the latest detection cache

3. JPEG encode worker
   - compresses GUI stream frames into one UDP-safe JPEG packet

4. UDP send worker
   - sends image packet first
   - sends detection packet second
   - keeps socket delay off the main loop

## 5. Packet compatibility

The live camera sender keeps the same GUI protocol as the current EO path.

### Image packet

```text
20-byte header + JPEG bytes
```

Header:

```text
!QIIHH
stampNs, frameId, jpegLength, width, height
```

### Detection packet

```text
DETS + JSON
```

### Status packet

```text
STAT + JSON
```

Because the packet format is unchanged, the current GUI receiver can keep working without a new parser.

## 6. Low-latency design choices

The new sender reduces delay using these rules:

1. latest-frame oriented flow
   - if a worker is busy, the next loop does not build a long queue
   - older frames are naturally dropped instead of piling up

2. small capture buffer
   - `CAMERA_BUFFER_SIZE=1` by default
   - reduces stale frames sitting inside the capture backend

3. asynchronous JPEG encode
   - `ENABLE_ASYNC_ENCODING=true` by default

4. asynchronous UDP send
   - `ENABLE_ASYNC_UDP_SEND=true` by default

5. optional frame skipping
   - if the loop falls behind target FPS, `grab()` can skip old frames

6. separate inference source scaling
   - GUI stream resolution and YOLO inference resolution can be tuned separately

## 7. Camera source support

`CAMERA_SOURCE` can be:

- a Linux device path like `/dev/video0`
- a numeric index like `0`
- another OpenCV-supported string source

The script also supports:

- `CAMERA_BACKEND=any|v4l2|gstreamer|ffmpeg`
- `CAMERA_WIDTH`
- `CAMERA_HEIGHT`
- `CAMERA_FPS`
- `CAMERA_FOURCC`

## 8. Default EO live run command

On Jetson:

```bash
cd ~/LIG_DNA_GUI/JetsonThor.MevaYoloDocker
bash ./run_live_camera_yolo.sh --build
```

The default assumptions are:

- EO live camera goes to GUI port `5000`
- camera device is `/dev/video0`
- GUI host is `192.168.1.94`

## 9. Useful overrides

Example with explicit EO camera device:

```bash
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
CAMERA_SOURCE=/dev/video0 \
CAMERA_DEVICE=/dev/video0 \
CAMERA_BACKEND=v4l2 \
CAMERA_WIDTH=1280 \
CAMERA_HEIGHT=720 \
CAMERA_FPS=30 \
STREAM_MAX_WIDTH=854 \
STREAM_MAX_HEIGHT=480 \
JPEG_QUALITY=45 \
DETECTION_INTERVAL_SECONDS=0.5 \
bash ./run_live_camera_yolo.sh
```

Example with more aggressive low-latency tuning:

```bash
GUI_HOST=192.168.1.94 \
GUI_PORT=5000 \
CAMERA_SOURCE=/dev/video0 \
CAMERA_DEVICE=/dev/video0 \
CAMERA_BUFFER_SIZE=1 \
STREAM_TARGET_FPS=30 \
ENABLE_FRAME_SKIP=true \
MAX_FRAME_SKIP=8 \
ENABLE_ASYNC_ENCODING=true \
ENABLE_ASYNC_UDP_SEND=true \
UDP_SEND_BUFFER_BYTES=4194304 \
bash ./run_live_camera_yolo.sh
```

## 10. Relation to IR

This branch does not replace the IR path.

IR still uses the ROS2 bridge path:

```text
IR camera -> ZYBO10 -> Jetson ROS2 -> ros_ir_to_gui_bridge.py -> GUI port 5001
```

So after this branch:

- EO live camera can use the new low-latency Docker sender on port `5000`
- IR can continue using the ROS2 bridge on port `5001`
- MEVA demo can still be used when needed

## 11. What to verify

When the EO live camera sender is running, check:

1. Jetson console
   - camera source opened successfully
   - YOLO model loaded
   - no repeated reopen messages

2. GUI system log
   - EO receiver waiting on port `5000`
   - EO first frame received

3. GUI display
   - EO live video updates continuously
   - detection overlay is still drawn by GUI

## 12. Summary

This branch keeps the old MEVA demo path and adds a separate live camera path optimized for real-time EO streaming.

The main improvement is not a new packet protocol. The main improvement is removing demo-video logic and keeping the latest-frame pipeline short:

```text
camera -> worker split -> GUI
```

That is the correct direction if the next step is real EO/IR camera integration rather than clip playback.
