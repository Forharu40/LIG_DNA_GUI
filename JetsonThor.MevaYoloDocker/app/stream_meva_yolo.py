import os
import socket
import struct
import time
from pathlib import Path

import cv2
from ultralytics import YOLO


GUI_HOST = os.getenv("GUI_HOST", "127.0.0.1")
GUI_PORT = int(os.getenv("GUI_PORT", "5000"))
SOURCE_ROOT = Path(os.getenv("SOURCE_ROOT", "/data/MEVA"))
VIDEO_PATH = os.getenv("VIDEO_PATH", "").strip()
MODEL_PATH = os.getenv("MODEL_PATH", "yolo11n.pt").strip()
CONFIDENCE = float(os.getenv("CONFIDENCE", "0.25"))
JPEG_QUALITY = int(os.getenv("JPEG_QUALITY", "85"))
LOOP_VIDEO = os.getenv("LOOP_VIDEO", "true").lower() in {"1", "true", "yes", "on"}
CLIP_START_SECONDS = float(os.getenv("CLIP_START_SECONDS", "0"))
CLIP_DURATION_SECONDS = float(os.getenv("CLIP_DURATION_SECONDS", "15"))
SAMPLE_INTERVAL_SECONDS = float(os.getenv("SAMPLE_INTERVAL_SECONDS", "43200"))
BOX_THICKNESS = int(os.getenv("BOX_THICKNESS", "2"))
FONT_SCALE = float(os.getenv("FONT_SCALE", "0.6"))
LABEL_THICKNESS = int(os.getenv("LABEL_THICKNESS", "2"))

VIDEO_EXTENSIONS = {".mp4", ".avi", ".mov", ".mkv", ".m4v"}


def find_video_file() -> Path:
    if VIDEO_PATH:
        path = Path(VIDEO_PATH)
        if not path.exists():
            raise FileNotFoundError(f"VIDEO_PATH does not exist: {path}")
        return path

    if not SOURCE_ROOT.exists():
        raise FileNotFoundError(f"SOURCE_ROOT does not exist: {SOURCE_ROOT}")

    for path in sorted(SOURCE_ROOT.rglob("*")):
        if path.is_file() and path.suffix.lower() in VIDEO_EXTENSIONS:
            return path

    raise FileNotFoundError(f"No video file was found under {SOURCE_ROOT}")


def build_label(class_name: str, track_id: int | None, fallback_index: int) -> str:
    object_index = track_id if track_id is not None else fallback_index
    return f"{class_name} object{object_index}"


def build_video_packet(
    jpeg_bytes: bytes,
    frame_width: int,
    frame_height: int,
    clip_index: int,
    clip_count: int,
    segment_start_seconds: float,
    segment_end_seconds: float,
    current_playback_seconds: float,
) -> bytes:
    header = struct.pack(
        ">4sIHHIIIII",
        b"MEVA",
        len(jpeg_bytes),
        frame_width,
        frame_height,
        clip_index,
        clip_count,
        int(max(0, segment_start_seconds)),
        int(max(0, segment_end_seconds)),
        int(max(0, current_playback_seconds)),
    )
    return header + jpeg_bytes


def build_clip_start_times(video_duration_seconds: float) -> list[float]:
    if video_duration_seconds <= 0:
        return [max(0.0, CLIP_START_SECONDS)]

    if SAMPLE_INTERVAL_SECONDS <= 0:
        return [max(0.0, min(CLIP_START_SECONDS, video_duration_seconds))]

    start_times: list[float] = []
    current_start = max(0.0, CLIP_START_SECONDS)
    while current_start < video_duration_seconds:
        start_times.append(current_start)
        current_start += SAMPLE_INTERVAL_SECONDS

    if not start_times:
        start_times.append(0.0)

    return start_times


def main() -> None:
    video_path = find_video_file()
    print(f"Using video: {video_path}")
    print(f"Streaming to GUI: {GUI_HOST}:{GUI_PORT}")
    print(f"Using model: {MODEL_PATH}")
    print(
        f"Clip settings: start={CLIP_START_SECONDS:.1f}s, "
        f"duration={CLIP_DURATION_SECONDS:.1f}s, "
        f"interval={SAMPLE_INTERVAL_SECONDS:.1f}s"
    )

    model = YOLO(MODEL_PATH)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    try:
        while True:
            capture = cv2.VideoCapture(str(video_path))
            if not capture.isOpened():
                raise RuntimeError(f"Could not open video: {video_path}")

            fps = capture.get(cv2.CAP_PROP_FPS)
            frame_delay = 1.0 / fps if fps and fps > 0 else 0.04
            frame_count = capture.get(cv2.CAP_PROP_FRAME_COUNT)
            video_duration_seconds = 0.0
            if fps and fps > 0 and frame_count and frame_count > 0:
                video_duration_seconds = frame_count / fps

            clip_start_times = build_clip_start_times(video_duration_seconds)

            for clip_index, clip_start_seconds in enumerate(clip_start_times, start=1):
                clip_start_msec = max(0.0, clip_start_seconds * 1000.0)
                clip_end_msec = None
                if CLIP_DURATION_SECONDS > 0:
                    clip_end_msec = clip_start_msec + (CLIP_DURATION_SECONDS * 1000.0)

                print(
                    f"Playing clip {clip_index}/{len(clip_start_times)} "
                    f"from {clip_start_seconds:.1f}s"
                )

                capture.set(cv2.CAP_PROP_POS_MSEC, clip_start_msec)

                while True:
                    if clip_end_msec is not None:
                        current_msec = capture.get(cv2.CAP_PROP_POS_MSEC)
                        if current_msec >= clip_end_msec:
                            break

                    ok, frame = capture.read()
                    if not ok:
                        break

                    results = model.track(frame, persist=True, conf=CONFIDENCE, verbose=False)
                    annotated = frame.copy()

                    if results:
                        result = results[0]
                        boxes = result.boxes
                        names = result.names

                        if boxes is not None and len(boxes) > 0:
                            xyxy = boxes.xyxy.cpu().numpy().astype(int)
                            cls_ids = boxes.cls.cpu().numpy().astype(int)
                            track_ids = boxes.id.cpu().numpy().astype(int) if boxes.id is not None else None

                            for index, (box, cls_id) in enumerate(zip(xyxy, cls_ids), start=1):
                                x1, y1, x2, y2 = box.tolist()
                                track_id = int(track_ids[index - 1]) if track_ids is not None else None
                                class_name = names.get(cls_id, str(cls_id))
                                label = build_label(class_name, track_id, index)

                                cv2.rectangle(
                                    annotated,
                                    (x1, y1),
                                    (x2, y2),
                                    (0, 255, 0),
                                    BOX_THICKNESS,
                                )

                                text_origin_y = y1 - 10 if y1 > 30 else y1 + 22
                                (text_width, text_height), baseline = cv2.getTextSize(
                                    label,
                                    cv2.FONT_HERSHEY_SIMPLEX,
                                    FONT_SCALE,
                                    LABEL_THICKNESS,
                                )
                                text_box_top = max(0, text_origin_y - text_height - baseline - 4)
                                text_box_bottom = min(
                                    annotated.shape[0],
                                    text_origin_y + baseline + 4,
                                )
                                text_box_right = min(
                                    annotated.shape[1],
                                    x1 + text_width + 10,
                                )

                                cv2.rectangle(
                                    annotated,
                                    (x1, text_box_top),
                                    (text_box_right, text_box_bottom),
                                    (0, 96, 0),
                                    -1,
                                )
                                cv2.putText(
                                    annotated,
                                    label,
                                    (x1, text_origin_y),
                                    cv2.FONT_HERSHEY_SIMPLEX,
                                    FONT_SCALE,
                                    (255, 255, 255),
                                    LABEL_THICKNESS,
                                    cv2.LINE_AA,
                                )

                    ok, encoded = cv2.imencode(
                        ".jpg",
                        annotated,
                        [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
                    )
                    if ok:
                        current_playback_seconds = capture.get(cv2.CAP_PROP_POS_MSEC) / 1000.0
                        packet = build_video_packet(
                            encoded.tobytes(),
                            annotated.shape[1],
                            annotated.shape[0],
                            clip_index,
                            len(clip_start_times),
                            clip_start_seconds,
                            clip_start_seconds + CLIP_DURATION_SECONDS,
                            current_playback_seconds,
                        )
                        sock.sendto(packet, (GUI_HOST, GUI_PORT))

                    time.sleep(frame_delay)

            capture.release()

            if not LOOP_VIDEO:
                break

            print("MEVA video segment cycle completed. Restarting from the first sampled 15-second video segment.")
    finally:
        sock.close()


if __name__ == "__main__":
    main()
