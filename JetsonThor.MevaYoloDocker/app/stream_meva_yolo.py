import os
import re
import socket
import struct
import time
import math
from dataclasses import dataclass
from datetime import datetime, timedelta
from pathlib import Path
import json

import cv2
from ultralytics import YOLO


LEGACY_IMAGE_HEADER_SIZE = 20
DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"
GUI_HOST = os.getenv("GUI_HOST", "127.0.0.1")
GUI_PORT = int(os.getenv("GUI_PORT", "5000"))
SOURCE_ROOT = Path(os.getenv("SOURCE_ROOT", "/data/MEVA"))
VIDEO_PATH = os.getenv("VIDEO_PATH", "").strip()
MODEL_PATH = os.getenv("MODEL_PATH", "yolo11n.pt").strip()
CONFIDENCE = float(os.getenv("CONFIDENCE", "0.25"))
JPEG_QUALITY = int(os.getenv("JPEG_QUALITY", "85"))
LOOP_VIDEO = os.getenv("LOOP_VIDEO", "true").lower() in {"1", "true", "yes", "on"}
CLIP_START_SECONDS = float(os.getenv("CLIP_START_SECONDS", "0"))
CLIP_DURATION_SECONDS = float(os.getenv("CLIP_DURATION_SECONDS", "10"))
SAMPLE_INTERVAL_SECONDS = float(os.getenv("SAMPLE_INTERVAL_SECONDS", "1800"))
SAMPLE_START_RATIO = float(os.getenv("SAMPLE_START_RATIO", "0.8"))
BOX_THICKNESS = int(os.getenv("BOX_THICKNESS", "2"))
FONT_SCALE = float(os.getenv("FONT_SCALE", "0.6"))
LABEL_THICKNESS = int(os.getenv("LABEL_THICKNESS", "2"))
MAX_UDP_BYTES = int(os.getenv("MAX_UDP_BYTES", "60000"))

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


def reorder_entries_by_start_ratio(sampled_entries: list[VideoEntry]) -> tuple[list[VideoEntry], int]:
    if not sampled_entries:
        return sampled_entries, 0

    normalized_ratio = min(max(SAMPLE_START_RATIO, 0.0), 1.0)
    start_index = math.floor(len(sampled_entries) * normalized_ratio)
    if start_index >= len(sampled_entries):
        start_index = len(sampled_entries) - 1

    if start_index <= 0:
        return sampled_entries, 0

    return sampled_entries[start_index:] + sampled_entries[:start_index], start_index


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
    clip_index: int,
    clip_count: int,
    segment_start_seconds: float,
    segment_end_seconds: float,
    current_playback_seconds: float,
    cycle_index: int,
) -> bytes:
    return struct.pack(
        ">4sIHHIIIIII",
        b"MEVA",
        0,
        0,
        0,
        clip_index,
        clip_count,
        int(max(0, segment_start_seconds)),
        int(max(0, segment_end_seconds)),
        int(max(0, current_playback_seconds)),
        max(0, cycle_index),
    )


def build_image_packet(
    encoded_bytes: bytes,
    width: int,
    height: int,
    frame_index: int,
    frame_stamp_ns: int,
) -> bytes:
    header = struct.pack(
        "!QIIHH",
        max(0, frame_stamp_ns),
        max(0, frame_index),
        len(encoded_bytes),
        max(0, width),
        max(0, height),
    )
    return header + encoded_bytes


def build_detection_packet(
    frame_stamp_ns: int,
    frame_index: int,
    width: int,
    height: int,
    detections: list[dict],
) -> bytes:
    payload = {
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
        "width": max(0, width),
        "height": max(0, height),
        "detections": detections,
    }
    return DETECTION_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def build_status_packet(
    enabled: bool,
    model_loaded: bool,
    conf_threshold: float,
    last_error: str,
    source: str,
    frame_stamp_ns: int = 0,
    frame_index: int = 0,
) -> bytes:
    payload = {
        "enabled": enabled,
        "modelLoaded": model_loaded,
        "confThreshold": conf_threshold,
        "lastError": last_error,
        "source": source,
        "stampNs": max(0, frame_stamp_ns),
        "frameId": max(0, frame_index),
    }
    return STATUS_PACKET_MAGIC + json.dumps(payload, separators=(",", ":")).encode("utf-8")


def encode_frame_for_udp(frame) -> bytes | None:
    max_image_payload_bytes = max(1024, MAX_UDP_BYTES - LEGACY_IMAGE_HEADER_SIZE)
    quality_attempts = [
        max(25, JPEG_QUALITY),
        max(25, min(JPEG_QUALITY, 70)),
        55,
        40,
    ]
    scale_attempts = [1.0, 0.85, 0.7, 0.55]

    for scale in scale_attempts:
        working_frame = frame
        if scale < 0.999:
            new_width = max(2, int(frame.shape[1] * scale))
            new_height = max(2, int(frame.shape[0] * scale))
            working_frame = cv2.resize(frame, (new_width, new_height))

        for quality in quality_attempts:
            ok, encoded = cv2.imencode(
                ".jpg",
                working_frame,
                [int(cv2.IMWRITE_JPEG_QUALITY), quality],
            )
            if ok and len(encoded) <= max_image_payload_bytes:
                return encoded.tobytes()

    return None


def detect_objects(model: YOLO, frame) -> list[dict]:
    results = model.track(frame, persist=True, conf=CONFIDENCE, verbose=False)
    if not results:
        return []

    result = results[0]
    boxes = result.boxes
    names = result.names

    if boxes is None or len(boxes) == 0:
        return []

    xyxy = boxes.xyxy.cpu().numpy().astype(int)
    cls_ids = boxes.cls.cpu().numpy().astype(int)
    scores = boxes.conf.cpu().numpy().astype(float)
    track_ids = boxes.id.cpu().numpy().astype(int) if boxes.id is not None else None
    detections: list[dict] = []

    for index, (box, cls_id, score) in enumerate(zip(xyxy, cls_ids, scores), start=1):
        x1, y1, x2, y2 = box.tolist()
        track_id = int(track_ids[index - 1]) if track_ids is not None else None
        class_name = names.get(cls_id, str(cls_id))
        detections.append(
            {
                "className": class_name,
                "score": float(score),
                "x1": float(x1),
                "y1": float(y1),
                "x2": float(x2),
                "y2": float(y2),
                "objectId": track_id if track_id is not None else index,
            }
        )

    return detections


def main() -> None:
    sampled_entries = build_sampled_entries()
    sampled_entries, start_index = reorder_entries_by_start_ratio(sampled_entries)
    print(f"Selected {len(sampled_entries)} sampled video entries from {SOURCE_ROOT}")
    print(
        f"Starting playback from sampled entry {start_index + 1}/{len(sampled_entries)} "
        f"(ratio {SAMPLE_START_RATIO:.2f})"
    )
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
    print(f"Sample start ratio: {SAMPLE_START_RATIO:.2f}")
    print(f"Max UDP payload target: {MAX_UDP_BYTES} bytes")

    model = YOLO(MODEL_PATH)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    status_packet = build_status_packet(True, True, CONFIDENCE, "", str(SOURCE_ROOT))
    sock.sendto(status_packet, (GUI_HOST, GUI_PORT))

    try:
        cycle_index = 0
        frame_index = 0
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

                    metadata_packet = build_video_packet(
                        clip_index,
                        len(sampled_entries),
                        timeline_segment_start,
                        timeline_segment_end,
                        timeline_segment_start,
                        cycle_index,
                    )
                    sock.sendto(metadata_packet, (GUI_HOST, GUI_PORT))

                    capture.set(cv2.CAP_PROP_POS_MSEC, clip_start_msec)

                    while True:
                        if clip_end_msec is not None:
                            current_msec = capture.get(cv2.CAP_PROP_POS_MSEC)
                            if current_msec >= clip_end_msec:
                                break

                        ok, frame = capture.read()
                        if not ok:
                            break

                        detections = detect_objects(model, frame)
                        encoded_bytes = encode_frame_for_udp(frame)
                        if encoded_bytes is None:
                            time.sleep(frame_delay)
                            continue

                        frame_index += 1
                        current_frame_stamp_ns = time.time_ns()
                        image_packet = build_image_packet(
                            encoded_bytes,
                            frame.shape[1],
                            frame.shape[0],
                            frame_index,
                            current_frame_stamp_ns,
                        )
                        detection_packet = build_detection_packet(
                            current_frame_stamp_ns,
                            frame_index,
                            frame.shape[1],
                            frame.shape[0],
                            detections,
                        )
                        sock.sendto(image_packet, (GUI_HOST, GUI_PORT))
                        sock.sendto(detection_packet, (GUI_HOST, GUI_PORT))

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
