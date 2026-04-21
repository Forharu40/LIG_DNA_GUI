import os
import statistics
import time
from pathlib import Path

import cv2


SOURCE_ROOT = Path(os.getenv("SOURCE_ROOT", "/data/MEVA"))
VIDEO_PATH = os.getenv("VIDEO_PATH", "").strip()
JPEG_QUALITY = int(os.getenv("JPEG_QUALITY", "35"))
BENCHMARK_FRAMES = max(1, int(os.getenv("BENCHMARK_FRAMES", "180")))
WARMUP_FRAMES = max(0, int(os.getenv("WARMUP_FRAMES", "20")))
BENCHMARK_SIZE_A = os.getenv("BENCHMARK_SIZE_A", "1280x720")
BENCHMARK_SIZE_B = os.getenv("BENCHMARK_SIZE_B", "480x270")
BENCHMARK_OUTPUT_DIR = Path(os.getenv("BENCHMARK_OUTPUT_DIR", "/benchmark-output"))
VIDEO_EXTENSIONS = {".mp4", ".avi", ".mov", ".mkv", ".m4v"}


def collect_video_paths() -> list[Path]:
    if VIDEO_PATH:
        path = Path(VIDEO_PATH)
        if not path.exists():
            raise FileNotFoundError(f"VIDEO_PATH does not exist: {path}")
        return [path]

    if not SOURCE_ROOT.exists():
        raise FileNotFoundError(f"SOURCE_ROOT does not exist: {SOURCE_ROOT}")

    return sorted(
        path
        for path in SOURCE_ROOT.rglob("*")
        if path.is_file() and path.suffix.lower() in VIDEO_EXTENSIONS
    )


def resize_exact(frame, width: int, height: int):
    return cv2.resize(frame, (width, height), interpolation=cv2.INTER_AREA)


def parse_size(value: str) -> tuple[int, int]:
    normalized = value.lower().replace(" ", "")
    if "x" not in normalized:
        raise ValueError(f"Invalid size format: {value}. Use WIDTHxHEIGHT, for example 1280x720.")

    width_text, height_text = normalized.split("x", maxsplit=1)
    width = int(width_text)
    height = int(height_text)
    if width <= 0 or height <= 0:
        raise ValueError(f"Invalid size: {value}")

    return width, height


def measure_encode(frame, frame_count: int) -> tuple[list[float], list[int]]:
    encode_times_ms: list[float] = []
    encoded_sizes: list[int] = []

    for _ in range(WARMUP_FRAMES):
        cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY])

    for _ in range(frame_count):
        started_at = time.perf_counter()
        ok, encoded = cv2.imencode(
            ".jpg",
            frame,
            [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
        )
        elapsed_ms = (time.perf_counter() - started_at) * 1000.0
        if not ok:
            raise RuntimeError("cv2.imencode failed")

        encode_times_ms.append(elapsed_ms)
        encoded_sizes.append(len(encoded))

    return encode_times_ms, encoded_sizes


def encode_sample(frame) -> bytes:
    ok, encoded = cv2.imencode(
        ".jpg",
        frame,
        [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY],
    )
    if not ok:
        raise RuntimeError("cv2.imencode failed while writing sample")

    return encoded.tobytes()


def summarize(label: str, times_ms: list[float], sizes: list[int]) -> dict[str, float]:
    average_ms = statistics.fmean(times_ms)
    median_ms = statistics.median(times_ms)
    p95_ms = sorted(times_ms)[max(0, int(len(times_ms) * 0.95) - 1)]
    average_size = statistics.fmean(sizes)

    print(f"{label}")
    print(f"  average: {average_ms:.3f} ms ({average_ms / 1000.0:.6f} sec)")
    print(f"  median : {median_ms:.3f} ms ({median_ms / 1000.0:.6f} sec)")
    print(f"  p95    : {p95_ms:.3f} ms ({p95_ms / 1000.0:.6f} sec)")
    print(f"  jpeg   : {average_size / 1024.0:.1f} KiB average")

    return {
        "average_ms": average_ms,
        "median_ms": median_ms,
        "p95_ms": p95_ms,
        "average_size": average_size,
    }


def main() -> None:
    video_paths = collect_video_paths()
    if not video_paths:
        raise FileNotFoundError(f"No video files found below {SOURCE_ROOT}")

    video_path = video_paths[0]
    capture = cv2.VideoCapture(str(video_path))
    if not capture.isOpened():
        raise RuntimeError(f"Could not open video: {video_path}")

    ok, frame = capture.read()
    capture.release()
    if not ok:
        raise RuntimeError(f"Could not read first frame: {video_path}")

    width_a, height_a = parse_size(BENCHMARK_SIZE_A)
    width_b, height_b = parse_size(BENCHMARK_SIZE_B)
    frame_a = resize_exact(frame, width_a, height_a)
    frame_b = resize_exact(frame, width_b, height_b)

    print("JPEG encode benchmark")
    print(f"Video: {video_path}")
    print(f"JPEG quality: {JPEG_QUALITY}")
    print(f"Warmup frames: {WARMUP_FRAMES}")
    print(f"Measured frames: {BENCHMARK_FRAMES}")
    print(f"Sample output: {BENCHMARK_OUTPUT_DIR}")
    print()

    BENCHMARK_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    sample_a_path = BENCHMARK_OUTPUT_DIR / f"sample_{width_a}x{height_a}_q{JPEG_QUALITY}.jpg"
    sample_b_path = BENCHMARK_OUTPUT_DIR / f"sample_{width_b}x{height_b}_q{JPEG_QUALITY}.jpg"
    sample_a_path.write_bytes(encode_sample(frame_a))
    sample_b_path.write_bytes(encode_sample(frame_b))
    print(f"Wrote sample A: {sample_a_path}")
    print(f"Wrote sample B: {sample_b_path}")
    print()

    result_a = summarize(f"{width_a}x{height_a} encode", *measure_encode(frame_a, BENCHMARK_FRAMES))
    print()
    result_b = summarize(f"{width_b}x{height_b} encode", *measure_encode(frame_b, BENCHMARK_FRAMES))
    print()

    average_delta_ms = result_a["average_ms"] - result_b["average_ms"]
    median_delta_ms = result_a["median_ms"] - result_b["median_ms"]
    size_delta_kib = (result_a["average_size"] - result_b["average_size"]) / 1024.0

    print(f"Difference: {width_a}x{height_a} minus {width_b}x{height_b}")
    print(f"  average encode time delta: {average_delta_ms:.3f} ms ({average_delta_ms / 1000.0:.6f} sec)")
    print(f"  median encode time delta : {median_delta_ms:.3f} ms ({median_delta_ms / 1000.0:.6f} sec)")
    print(f"  average JPEG size delta  : {size_delta_kib:.1f} KiB")


if __name__ == "__main__":
    main()
