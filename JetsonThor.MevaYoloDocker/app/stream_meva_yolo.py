import os
import socket
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


def main() -> None:
    video_path = find_video_file()
    print(f"Using video: {video_path}")
    print(f"Streaming to GUI: {GUI_HOST}:{GUI_PORT}")
    print(f"Using model: {MODEL_PATH}")

    model = YOLO(MODEL_PATH)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    try:
        while True:
            capture = cv2.VideoCapture(str(video_path))
            if not capture.isOpened():
                raise RuntimeError(f"Could not open video: {video_path}")

            fps = capture.get(cv2.CAP_PROP_FPS)
            frame_delay = 1.0 / fps if fps and fps > 0 else 0.04

            while True:
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

                            cv2.rectangle(annotated, (x1, y1), (x2, y2), (0, 255, 0), 2)
                            text_origin_y = y1 - 8 if y1 > 24 else y1 + 18
                            cv2.putText(
                                annotated,
                                label,
                                (x1, text_origin_y),
                                cv2.FONT_HERSHEY_SIMPLEX,
                                0.5,
                                (255, 255, 255),
                                1,
                                cv2.LINE_AA,
                            )

                ok, encoded = cv2.imencode(
                    ".jpg",
                    annotated,
                    [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
                )
                if ok:
                    sock.sendto(encoded.tobytes(), (GUI_HOST, GUI_PORT))

                time.sleep(frame_delay)

            capture.release()

            if not LOOP_VIDEO:
                break

            print("Reached end of video. Restarting playback...")
    finally:
        sock.close()


if __name__ == "__main__":
    main()
