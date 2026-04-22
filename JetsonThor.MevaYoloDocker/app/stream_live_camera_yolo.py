import json
import os
import socket
import struct
import time
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass

import cv2
from ultralytics import YOLO


LEGACY_IMAGE_HEADER_SIZE = 20
DETECTION_PACKET_MAGIC = b"DETS"
STATUS_PACKET_MAGIC = b"STAT"
GUI_HOST = os.getenv("GUI_HOST", "127.0.0.1")
GUI_PORT = int(os.getenv("GUI_PORT", "5000"))
CAMERA_SOURCE = os.getenv("CAMERA_SOURCE", "/dev/video0").strip()
CAMERA_BACKEND = os.getenv("CAMERA_BACKEND", "any").strip().lower()
CAMERA_WIDTH = max(0, int(os.getenv("CAMERA_WIDTH", "1280")))
CAMERA_HEIGHT = max(0, int(os.getenv("CAMERA_HEIGHT", "720")))
CAMERA_FPS = max(0.0, float(os.getenv("CAMERA_FPS", "30")))
CAMERA_BUFFER_SIZE = max(0, int(os.getenv("CAMERA_BUFFER_SIZE", "1")))
CAMERA_FOURCC = os.getenv("CAMERA_FOURCC", "MJPG").strip().upper()
MAX_READ_FAILURES = max(1, int(os.getenv("MAX_READ_FAILURES", "30")))
REOPEN_DELAY_SECONDS = max(0.1, float(os.getenv("REOPEN_DELAY_SECONDS", "1.0")))
MODEL_PATH = os.getenv("MODEL_PATH", "yolo11s.pt").strip()
CONFIDENCE = float(os.getenv("CONFIDENCE", "0.60"))
INFERENCE_SIZE = int(os.getenv("INFERENCE_SIZE", "640"))
JPEG_QUALITY = int(os.getenv("JPEG_QUALITY", "45"))
MAX_UDP_BYTES = int(os.getenv("MAX_UDP_BYTES", "55000"))
TILE_OVERLAP_RATIO = float(os.getenv("TILE_OVERLAP_RATIO", "0.15"))
DETECTION_INTERVAL_SECONDS = max(0.1, float(os.getenv("DETECTION_INTERVAL_SECONDS", "0.5")))
STREAM_MAX_WIDTH = max(320, int(os.getenv("STREAM_MAX_WIDTH", "854")))
STREAM_MAX_HEIGHT = max(240, int(os.getenv("STREAM_MAX_HEIGHT", "480")))
STREAM_TARGET_FPS = float(os.getenv("STREAM_TARGET_FPS", "0"))
ENABLE_FRAME_SKIP = os.getenv("ENABLE_FRAME_SKIP", "true").lower() in {"1", "true", "yes", "on"}
MAX_FRAME_SKIP = max(0, int(os.getenv("MAX_FRAME_SKIP", "8")))
INFERENCE_SOURCE_MAX_WIDTH = max(0, int(os.getenv("INFERENCE_SOURCE_MAX_WIDTH", "0")))
INFERENCE_SOURCE_MAX_HEIGHT = max(0, int(os.getenv("INFERENCE_SOURCE_MAX_HEIGHT", "0")))
ENABLE_ASYNC_ENCODING = os.getenv("ENABLE_ASYNC_ENCODING", "true").lower() in {"1", "true", "yes", "on"}
ENABLE_ASYNC_UDP_SEND = os.getenv("ENABLE_ASYNC_UDP_SEND", "true").lower() in {"1", "true", "yes", "on"}
UDP_SEND_BUFFER_BYTES = max(0, int(os.getenv("UDP_SEND_BUFFER_BYTES", "4194304")))
ENABLE_TILE_INFERENCE = os.getenv("ENABLE_TILE_INFERENCE", "false").lower() in {"1", "true", "yes", "on"}
ALLOWED_CLASSES = {
    name.strip().lower()
    for name in os.getenv("ALLOWED_CLASSES", "").split(",")
    if name.strip()
}


@dataclass(frozen=True)
class DetectionResult:
    width: int
    height: int
    detections: list[dict]


@dataclass(frozen=True)
class EncodedFrame:
    encoded_bytes: bytes
    width: int
    height: int
    detection_width: int
    detection_height: int
    detections: list[dict]


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
            working_frame = cv2.resize(frame, (new_width, new_height), interpolation=cv2.INTER_AREA)

        for quality in quality_attempts:
            ok, encoded = cv2.imencode(
                ".jpg",
                working_frame,
                [int(cv2.IMWRITE_JPEG_QUALITY), quality],
            )
            if ok and len(encoded) <= max_image_payload_bytes:
                return encoded.tobytes()

    return None


def encode_frame_for_packet(
    frame,
    detection_width: int,
    detection_height: int,
    detections: list[dict],
) -> EncodedFrame | None:
    encoded_bytes = encode_frame_for_udp(frame)
    if encoded_bytes is None:
        return None

    return EncodedFrame(
        encoded_bytes,
        frame.shape[1],
        frame.shape[0],
        detection_width,
        detection_height,
        [dict(detection) for detection in detections],
    )


def send_encoded_frame_by_index(sock: socket.socket, encoded_frame: EncodedFrame, frame_index: int) -> None:
    current_frame_stamp_ns = time.time_ns()
    image_packet = build_image_packet(
        encoded_frame.encoded_bytes,
        encoded_frame.width,
        encoded_frame.height,
        frame_index,
        current_frame_stamp_ns,
    )
    detection_packet = build_detection_packet(
        current_frame_stamp_ns,
        frame_index,
        encoded_frame.detection_width,
        encoded_frame.detection_height,
        encoded_frame.detections,
    )
    sock.sendto(image_packet, (GUI_HOST, GUI_PORT))
    sock.sendto(detection_packet, (GUI_HOST, GUI_PORT))


def send_encoded_frame(sock: socket.socket, encoded_frame: EncodedFrame, frame_index: int) -> int:
    next_frame_index = frame_index + 1
    send_encoded_frame_by_index(sock, encoded_frame, next_frame_index)
    return next_frame_index


def complete_pending_send(sock: socket.socket, pending_send: Future[None]) -> None:
    try:
        pending_send.result()
    except Exception as exc:
        status_packet = build_status_packet(
            True,
            True,
            CONFIDENCE,
            f"UDP send failed: {exc}",
            f"camera:{CAMERA_SOURCE}",
        )
        sock.sendto(status_packet, (GUI_HOST, GUI_PORT))


def prepare_stream_frame(frame):
    source_height, source_width = frame.shape[:2]
    scale = min(
        1.0,
        STREAM_MAX_WIDTH / max(1, source_width),
        STREAM_MAX_HEIGHT / max(1, source_height),
    )
    if scale >= 0.999:
        return frame

    resized_width = max(2, int(source_width * scale))
    resized_height = max(2, int(source_height * scale))
    return cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_AREA)


def prepare_inference_frame(frame):
    source_height, source_width = frame.shape[:2]
    if INFERENCE_SOURCE_MAX_WIDTH <= 0 and INFERENCE_SOURCE_MAX_HEIGHT <= 0:
        return frame

    width_limit = INFERENCE_SOURCE_MAX_WIDTH if INFERENCE_SOURCE_MAX_WIDTH > 0 else source_width
    height_limit = INFERENCE_SOURCE_MAX_HEIGHT if INFERENCE_SOURCE_MAX_HEIGHT > 0 else source_height
    scale = min(
        1.0,
        width_limit / max(1, source_width),
        height_limit / max(1, source_height),
    )
    if scale >= 0.999:
        return frame

    resized_width = max(2, int(source_width * scale))
    resized_height = max(2, int(source_height * scale))
    return cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_AREA)


def pace_capture(capture, next_frame_time: float, frame_delay: float) -> float:
    if frame_delay <= 0:
        return time.monotonic()

    next_frame_time += frame_delay
    sleep_seconds = next_frame_time - time.monotonic()
    if sleep_seconds > 0:
        time.sleep(sleep_seconds)
        return next_frame_time

    if ENABLE_FRAME_SKIP:
        frames_to_skip = min(MAX_FRAME_SKIP, int(abs(sleep_seconds) / frame_delay))
        for _ in range(frames_to_skip):
            if not capture.grab():
                break

    return time.monotonic()


def calculate_iou(a: dict, b: dict) -> float:
    inter_x1 = max(a["x1"], b["x1"])
    inter_y1 = max(a["y1"], b["y1"])
    inter_x2 = min(a["x2"], b["x2"])
    inter_y2 = min(a["y2"], b["y2"])

    inter_width = max(0.0, inter_x2 - inter_x1)
    inter_height = max(0.0, inter_y2 - inter_y1)
    inter_area = inter_width * inter_height
    if inter_area <= 0:
        return 0.0

    area_a = max(0.0, a["x2"] - a["x1"]) * max(0.0, a["y2"] - a["y1"])
    area_b = max(0.0, b["x2"] - b["x1"]) * max(0.0, b["y2"] - b["y1"])
    denominator = area_a + area_b - inter_area
    if denominator <= 0:
        return 0.0

    return inter_area / denominator


def append_result_detections(
    results,
    output: list[dict],
    offset_x: int,
    offset_y: int,
    source_width: int,
    source_height: int,
) -> None:
    if not results:
        return

    result = results[0]
    boxes = result.boxes
    names = result.names

    if boxes is None or len(boxes) == 0:
        return

    xyxy = boxes.xyxy.cpu().numpy().astype(float)
    cls_ids = boxes.cls.cpu().numpy().astype(int)
    scores = boxes.conf.cpu().numpy().astype(float)

    for box, cls_id, score in zip(xyxy, cls_ids, scores):
        x1, y1, x2, y2 = box.tolist()
        mapped_x1 = max(0.0, min(source_width, x1 + offset_x))
        mapped_y1 = max(0.0, min(source_height, y1 + offset_y))
        mapped_x2 = max(0.0, min(source_width, x2 + offset_x))
        mapped_y2 = max(0.0, min(source_height, y2 + offset_y))
        if mapped_x2 - mapped_x1 < 2 or mapped_y2 - mapped_y1 < 2:
            continue

        output.append(
            {
                "className": names.get(cls_id, str(cls_id)),
                "score": float(score),
                "x1": mapped_x1,
                "y1": mapped_y1,
                "x2": mapped_x2,
                "y2": mapped_y2,
            }
        )


def suppress_duplicate_detections(detections: list[dict]) -> list[dict]:
    if not detections:
        return []

    sorted_detections = sorted(detections, key=lambda item: item["score"], reverse=True)
    filtered: list[dict] = []
    for candidate in sorted_detections:
        is_duplicate = False
        for kept in filtered:
            if kept["className"] != candidate["className"]:
                continue

            if calculate_iou(kept, candidate) >= 0.45:
                is_duplicate = True
                break

        if not is_duplicate:
            filtered.append(candidate)

    for index, detection in enumerate(filtered, start=1):
        detection["objectId"] = index

    return filtered


def filter_allowed_detections(detections: list[dict]) -> list[dict]:
    if not ALLOWED_CLASSES:
        return detections

    return [
        detection
        for detection in detections
        if str(detection["className"]).lower() in ALLOWED_CLASSES
    ]


def detect_objects(model: YOLO, frame) -> list[dict]:
    source_height, source_width = frame.shape[:2]
    detections: list[dict] = []

    full_frame_results = model.predict(
        frame,
        conf=CONFIDENCE,
        imgsz=INFERENCE_SIZE,
        verbose=False,
    )
    append_result_detections(full_frame_results, detections, 0, 0, source_width, source_height)

    if ENABLE_TILE_INFERENCE:
        overlap_x = int(source_width * TILE_OVERLAP_RATIO)
        overlap_y = int(source_height * TILE_OVERLAP_RATIO)
        tile_width = max(64, (source_width // 2) + overlap_x)
        tile_height = max(64, (source_height // 2) + overlap_y)
        step_x = max(32, tile_width - overlap_x)
        step_y = max(32, tile_height - overlap_y)

        for top in range(0, source_height, step_y):
            for left in range(0, source_width, step_x):
                bottom = min(source_height, top + tile_height)
                right = min(source_width, left + tile_width)
                tile = frame[top:bottom, left:right]
                if tile.size == 0:
                    continue

                tile_results = model.predict(
                    tile,
                    conf=CONFIDENCE,
                    imgsz=INFERENCE_SIZE,
                    verbose=False,
                )
                append_result_detections(tile_results, detections, left, top, source_width, source_height)

                if right >= source_width:
                    break

            if bottom >= source_height:
                break

    filtered = filter_allowed_detections(detections)
    return suppress_duplicate_detections(filtered)


def detect_objects_for_packet(model: YOLO, frame) -> DetectionResult:
    height, width = frame.shape[:2]
    return DetectionResult(width, height, detect_objects(model, frame))


def resolve_capture_backend() -> int:
    backend_map = {
        "any": cv2.CAP_ANY,
        "v4l2": getattr(cv2, "CAP_V4L2", cv2.CAP_ANY),
        "gstreamer": getattr(cv2, "CAP_GSTREAMER", cv2.CAP_ANY),
        "ffmpeg": getattr(cv2, "CAP_FFMPEG", cv2.CAP_ANY),
        "msmf": getattr(cv2, "CAP_MSMF", cv2.CAP_ANY),
    }
    return backend_map.get(CAMERA_BACKEND, cv2.CAP_ANY)


def resolve_capture_source():
    if CAMERA_SOURCE.isdigit():
        return int(CAMERA_SOURCE)
    return CAMERA_SOURCE


def open_camera_capture():
    source = resolve_capture_source()
    backend = resolve_capture_backend()
    capture = cv2.VideoCapture(source, backend) if backend != cv2.CAP_ANY else cv2.VideoCapture(source)
    if not capture.isOpened():
        raise RuntimeError(f"Could not open camera source: {CAMERA_SOURCE}")

    if CAMERA_BUFFER_SIZE > 0:
        capture.set(cv2.CAP_PROP_BUFFERSIZE, CAMERA_BUFFER_SIZE)
    if len(CAMERA_FOURCC) == 4:
        capture.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*CAMERA_FOURCC))
    if CAMERA_WIDTH > 0:
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
    if CAMERA_HEIGHT > 0:
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
    if CAMERA_FPS > 0:
        capture.set(cv2.CAP_PROP_FPS, CAMERA_FPS)

    return capture


def main() -> None:
    print(f"Streaming live camera to GUI: {GUI_HOST}:{GUI_PORT}")
    print(f"Camera source: {CAMERA_SOURCE}")
    print(f"Camera backend: {CAMERA_BACKEND}")
    print(f"Requested capture size: {CAMERA_WIDTH}x{CAMERA_HEIGHT}")
    print(f"Requested capture fps: {CAMERA_FPS:.2f}")
    print(f"Requested camera buffer size: {CAMERA_BUFFER_SIZE}")
    print(f"Requested camera fourcc: {CAMERA_FOURCC or 'default'}")
    print(f"Using model: {MODEL_PATH}")
    print(f"YOLO confidence threshold: {CONFIDENCE:.2f}")
    print(f"YOLO inference size: {INFERENCE_SIZE}")
    print(f"YOLO detection interval: every {DETECTION_INTERVAL_SECONDS:.2f} second(s)")
    print(f"Stream max size: {STREAM_MAX_WIDTH}x{STREAM_MAX_HEIGHT}")
    if INFERENCE_SOURCE_MAX_WIDTH > 0 or INFERENCE_SOURCE_MAX_HEIGHT > 0:
        print(f"Inference source max size: {INFERENCE_SOURCE_MAX_WIDTH}x{INFERENCE_SOURCE_MAX_HEIGHT}")
    else:
        print("Inference source size: original camera frame")
    if STREAM_TARGET_FPS > 0:
        print(f"Stream target FPS override: {STREAM_TARGET_FPS:.2f}")
    print(f"Frame skip enabled: {ENABLE_FRAME_SKIP} (max {MAX_FRAME_SKIP})")
    print(f"Async JPEG encoding enabled: {ENABLE_ASYNC_ENCODING}")
    print(f"Async UDP send enabled: {ENABLE_ASYNC_UDP_SEND}")
    print(f"UDP send buffer target: {UDP_SEND_BUFFER_BYTES} bytes")
    print(f"Tile inference enabled: {ENABLE_TILE_INFERENCE}")
    print(f"Allowed classes: {', '.join(sorted(ALLOWED_CLASSES))}")
    print(f"Max UDP payload target: {MAX_UDP_BYTES} bytes")

    model = YOLO(MODEL_PATH)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    if UDP_SEND_BUFFER_BYTES > 0:
        try:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, UDP_SEND_BUFFER_BYTES)
        except OSError as exc:
            print(f"Could not set UDP send buffer: {exc}")

    capture = open_camera_capture()
    source_fps = capture.get(cv2.CAP_PROP_FPS)
    effective_fps = STREAM_TARGET_FPS if STREAM_TARGET_FPS > 0 else source_fps
    frame_delay = 1.0 / effective_fps if effective_fps and effective_fps > 0 else 0.0

    status_packet = build_status_packet(True, True, CONFIDENCE, "", f"camera:{CAMERA_SOURCE}")
    sock.sendto(status_packet, (GUI_HOST, GUI_PORT))
    detection_executor = ThreadPoolExecutor(max_workers=1)
    encode_executor = ThreadPoolExecutor(max_workers=1) if ENABLE_ASYNC_ENCODING else None
    send_executor = ThreadPoolExecutor(max_workers=1) if ENABLE_ASYNC_UDP_SEND else None

    try:
        frame_index = 0
        latest_detections: list[dict] = []
        latest_detection_width = 0
        latest_detection_height = 0
        last_detection_monotonic: float | None = None
        pending_detection: Future[DetectionResult] | None = None
        pending_encoded_frame: Future[EncodedFrame | None] | None = None
        pending_send: Future[None] | None = None
        next_frame_time = time.monotonic()
        read_failures = 0

        while True:
            ok, frame = capture.read()
            if not ok or frame is None or frame.size == 0:
                read_failures += 1
                if read_failures >= MAX_READ_FAILURES:
                    message = f"Camera read failed. Reopening source after {read_failures} failures."
                    print(message)
                    status_packet = build_status_packet(
                        True,
                        True,
                        CONFIDENCE,
                        message,
                        f"camera:{CAMERA_SOURCE}",
                        frame_index=frame_index,
                    )
                    sock.sendto(status_packet, (GUI_HOST, GUI_PORT))
                    capture.release()
                    time.sleep(REOPEN_DELAY_SECONDS)
                    capture = open_camera_capture()
                    source_fps = capture.get(cv2.CAP_PROP_FPS)
                    effective_fps = STREAM_TARGET_FPS if STREAM_TARGET_FPS > 0 else source_fps
                    frame_delay = 1.0 / effective_fps if effective_fps and effective_fps > 0 else 0.0
                    next_frame_time = time.monotonic()
                    read_failures = 0
                else:
                    time.sleep(0.01)
                continue

            read_failures = 0
            stream_frame = prepare_stream_frame(frame)

            if pending_detection is not None and pending_detection.done():
                try:
                    detection_result = pending_detection.result()
                    latest_detections = detection_result.detections
                    latest_detection_width = detection_result.width
                    latest_detection_height = detection_result.height
                except Exception as exc:
                    latest_detections = []
                    latest_detection_width = 0
                    latest_detection_height = 0
                    status_packet = build_status_packet(
                        True,
                        True,
                        CONFIDENCE,
                        f"YOLO detection failed: {exc}",
                        f"camera:{CAMERA_SOURCE}",
                    )
                    sock.sendto(status_packet, (GUI_HOST, GUI_PORT))
                finally:
                    pending_detection = None

            if pending_encoded_frame is not None and pending_encoded_frame.done():
                try:
                    encoded_frame = pending_encoded_frame.result()
                    if encoded_frame is not None:
                        if send_executor is not None:
                            if pending_send is not None and pending_send.done():
                                complete_pending_send(sock, pending_send)
                                pending_send = None

                            if pending_send is None:
                                frame_index += 1
                                pending_send = send_executor.submit(
                                    send_encoded_frame_by_index,
                                    sock,
                                    encoded_frame,
                                    frame_index,
                                )
                        else:
                            frame_index = send_encoded_frame(sock, encoded_frame, frame_index)
                except Exception as exc:
                    status_packet = build_status_packet(
                        True,
                        True,
                        CONFIDENCE,
                        f"JPEG encoding failed: {exc}",
                        f"camera:{CAMERA_SOURCE}",
                    )
                    sock.sendto(status_packet, (GUI_HOST, GUI_PORT))
                finally:
                    pending_encoded_frame = None

            if pending_send is not None and pending_send.done():
                complete_pending_send(sock, pending_send)
                pending_send = None

            now_monotonic = time.monotonic()
            should_run_detection = (
                pending_detection is None and
                (
                    last_detection_monotonic is None or
                    (now_monotonic - last_detection_monotonic) >= DETECTION_INTERVAL_SECONDS
                )
            )

            if should_run_detection:
                inference_frame = prepare_inference_frame(frame)
                pending_detection = detection_executor.submit(
                    detect_objects_for_packet,
                    model,
                    inference_frame.copy(),
                )
                last_detection_monotonic = now_monotonic

            detections = latest_detections
            detection_width = latest_detection_width or stream_frame.shape[1]
            detection_height = latest_detection_height or stream_frame.shape[0]
            if encode_executor is not None:
                if pending_encoded_frame is None:
                    pending_encoded_frame = encode_executor.submit(
                        encode_frame_for_packet,
                        stream_frame.copy(),
                        detection_width,
                        detection_height,
                        [dict(detection) for detection in detections],
                    )
            else:
                encoded_frame = encode_frame_for_packet(
                    stream_frame,
                    detection_width,
                    detection_height,
                    detections,
                )
                if encoded_frame is not None:
                    if send_executor is not None:
                        if pending_send is not None and pending_send.done():
                            complete_pending_send(sock, pending_send)
                            pending_send = None

                        if pending_send is None:
                            frame_index += 1
                            pending_send = send_executor.submit(
                                send_encoded_frame_by_index,
                                sock,
                                encoded_frame,
                                frame_index,
                            )
                    else:
                        frame_index = send_encoded_frame(sock, encoded_frame, frame_index)

            next_frame_time = pace_capture(capture, next_frame_time, frame_delay)
    finally:
        capture.release()
        detection_executor.shutdown(wait=False, cancel_futures=True)
        if encode_executor is not None:
            encode_executor.shutdown(wait=False, cancel_futures=True)
        if send_executor is not None:
            send_executor.shutdown(wait=False, cancel_futures=True)
        sock.close()


if __name__ == "__main__":
    main()
