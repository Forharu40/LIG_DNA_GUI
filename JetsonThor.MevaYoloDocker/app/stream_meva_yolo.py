import os
import re
import socket
import struct
import time
from dataclasses import dataclass
from datetime import datetime, timedelta
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
TIMESTAMP_PATTERN = re.compile(r"(\d{4}-\d{2}-\d{2})\.(\d{2})-(\d{2})-(\d{2})")


@dataclass(frozen=True)
class VideoEntry:
    path: Path
    start_time: datetime | None
    relative_start_seconds: float


def parse_video_start_time(path: Path) -> datetime | None:
    match = TIMESTAMP_PATTERN.search(path.name)
    if not match:
        return None

    date_part, hour, minute, second = match.groups()
    try:
        return datetime.strptime(
            f"{date_part} {hour}:{minute}:{second}",
            "%Y-%m-%d %H:%M:%S",
        )
    except ValueError:
        return None


def collect_video_paths() -> list[Path]:
    if VIDEO_PATH:
        path = Path(VIDEO_PATH)
        if not path.exists():
            raise FileNotFoundError(f"VIDEO_PATH does not exist: {path}")
        return [path]

    if not SOURCE_ROOT.exists():
        raise FileNotFoundError(f"SOURCE_ROOT does not exist: {SOURCE_ROOT}")

    video_paths = [
        path
        for path in sorted(SOURCE_ROOT.rglob("*"))
        if path.is_file() and path.suffix.lower() in VIDEO_EXTENSIONS
    ]

    if not video_paths:
        raise FileNotFoundError(f"No video file was found under {SOURCE_ROOT}")

    return video_paths


def build_sampled_entries() -> list[VideoEntry]:
    video_paths = collect_video_paths()

    if len(video_paths) == 1:
        return [VideoEntry(video_paths[0], None, 0.0)]

    entries_with_time = []
    for path in video_paths:
        start_time = parse_video_start_time(path)
        if start_time is not None:
            entries_with_time.append((path, start_time))

    if len(entries_with_time) < 2:
        return [VideoEntry(video_paths[0], None, 0.0)]

    entries_with_time.sort(key=lambda item: item[1])
    timeline_origin = entries_with_time[0][1]

    sampled_entries: list[VideoEntry] = []
    current_index = 0
    next_target_time = timeline_origin

    while current_index < len(entries_with_time):
        while (
            current_index < len(entries_with_time)
            and entries_with_time[current_index][1] < next_target_time
        ):
            current_index += 1

        if current_index >= len(entries_with_time):
            break

        selected_path, selected_time = entries_with_time[current_index]
        relative_start_seconds = (selected_time - timeline_origin).total_seconds()
        sampled_entries.append(
            VideoEntry(
                path=selected_path,
                start_time=selected_time,
                relative_start_seconds=max(0.0, relative_start_seconds),
            )
        )

        if SAMPLE_INTERVAL_SECONDS <= 0:
            current_index += 1
            next_target_time = selected_time
        else:
            next_target_time = selected_time + timedelta(seconds=SAMPLE_INTERVAL_SECONDS)
            current_index += 1

    if not sampled_entries:
        return [VideoEntry(video_paths[0], None, 0.0)]

    return sampled_entries


def build_label(class_name: str, track_id: int | None, fallback_index: int) -> str:
    object_index = track_id if track_id is not None else fallback_index
    return f"{class_name} object{object_index}"


def format_hms(total_seconds: float) -> str:
    total_seconds = max(0, int(total_seconds))
    hours = total_seconds // 3600
    minutes = (total_seconds % 3600) // 60
    seconds = total_seconds % 60
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"


def build_video_packet(
    jpeg_bytes: bytes,
    frame_width: int,
    frame_height: int,
    clip_index: int,
    clip_count: int,
    segment_start_seconds: float,
    segment_end_seconds: float,
    current_playback_seconds: float,
    cycle_index: int,
) -> bytes:
    header = struct.pack(
        ">4sIHHIIIIII",
        b"MEVA",
        len(jpeg_bytes),
        frame_width,
        frame_height,
        clip_index,
        clip_count,
        int(max(0, segment_start_seconds)),
        int(max(0, segment_end_seconds)),
        int(max(0, current_playback_seconds)),
        max(0, cycle_index),
    )
    return header + jpeg_bytes


def annotate_frame(model: YOLO, frame) -> any:
    results = model.track(frame, persist=True, conf=CONFIDENCE, verbose=False)
    annotated = frame.copy()

    if not results:
        return annotated

    result = results[0]
    boxes = result.boxes
    names = result.names

    if boxes is None or len(boxes) == 0:
        return annotated

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

    return annotated


def main() -> None:
    sampled_entries = build_sampled_entries()
    print(f"Selected {len(sampled_entries)} sampled video entries from {SOURCE_ROOT}")
    for index, entry in enumerate(sampled_entries, start=1):
        if entry.start_time is None:
            print(f"  [{index}] {entry.path}")
        else:
            print(
                f"  [{index}] {entry.path} "
                f"(timeline {format_hms(entry.relative_start_seconds)})"
            )

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
        cycle_index = 0
        while True:
            for clip_index, entry in enumerate(sampled_entries, start=1):
                capture = cv2.VideoCapture(str(entry.path))
                if not capture.isOpened():
                    raise RuntimeError(f"Could not open video: {entry.path}")

                try:
                    fps = capture.get(cv2.CAP_PROP_FPS)
                    frame_delay = 1.0 / fps if fps and fps > 0 else 0.04
                    clip_start_msec = max(0.0, CLIP_START_SECONDS * 1000.0)
                    clip_end_msec = None
                    if CLIP_DURATION_SECONDS > 0:
                        clip_end_msec = clip_start_msec + (CLIP_DURATION_SECONDS * 1000.0)

                    timeline_segment_start = entry.relative_start_seconds + CLIP_START_SECONDS
                    timeline_segment_end = timeline_segment_start + CLIP_DURATION_SECONDS

                    print(
                        f"Playing sampled clip {clip_index}/{len(sampled_entries)} "
                        f"from {entry.path.name} at timeline {format_hms(timeline_segment_start)}"
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

                        annotated = annotate_frame(model, frame)

                        ok, encoded = cv2.imencode(
                            ".jpg",
                            annotated,
                            [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
                        )
                        if ok:
                            current_playback_seconds = (
                                entry.relative_start_seconds
                                + (capture.get(cv2.CAP_PROP_POS_MSEC) / 1000.0)
                            )
                            packet = build_video_packet(
                                encoded.tobytes(),
                                annotated.shape[1],
                                annotated.shape[0],
                                clip_index,
                                len(sampled_entries),
                                timeline_segment_start,
                                timeline_segment_end,
                                current_playback_seconds,
                                cycle_index,
                            )
                            sock.sendto(packet, (GUI_HOST, GUI_PORT))

                        time.sleep(frame_delay)
                finally:
                    capture.release()

            if not LOOP_VIDEO:
                break

            print("MEVA video segment cycle completed. Restarting from the first sampled timeline entry.")
            cycle_index += 1
    finally:
        sock.close()


if __name__ == "__main__":
    main()
