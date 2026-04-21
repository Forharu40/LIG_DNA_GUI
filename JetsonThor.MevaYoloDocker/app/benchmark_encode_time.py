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
BENCHMARK_QUALITY_A = int(os.getenv("BENCHMARK_QUALITY_A", str(JPEG_QUALITY)))
BENCHMARK_QUALITY_B = int(os.getenv("BENCHMARK_QUALITY_B", str(JPEG_QUALITY)))
BENCHMARK_MAX_BYTES_A = int(os.getenv("BENCHMARK_MAX_BYTES_A", "0"))
BENCHMARK_MAX_BYTES_B = int(os.getenv("BENCHMARK_MAX_BYTES_B", "0"))
BENCHMARK_ADAPTIVE_A = os.getenv("BENCHMARK_ADAPTIVE_A", "false").lower() in {"1", "true", "yes", "on"}
BENCHMARK_ADAPTIVE_B = os.getenv("BENCHMARK_ADAPTIVE_B", "false").lower() in {"1", "true", "yes", "on"}
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


def encode_frame(frame, quality: int, max_bytes: int, adaptive: bool) -> bytes | None:
    if not adaptive:
        ok, encoded = cv2.imencode(
            ".jpg",
            frame,
            [int(cv2.IMWRITE_JPEG_QUALITY), quality],
        )
        if not ok:
            raise RuntimeError("cv2.imencode failed")

        return encoded.tobytes()

    quality_attempts = [
        max(25, quality),
        max(25, min(quality, 70)),
        55,
        40,
    ]
    scale_attempts = [1.0, 0.85, 0.7, 0.55]
    max_payload_bytes = max(1024, max_bytes) if max_bytes > 0 else 0

    for scale in scale_attempts:
        working_frame = frame
        if scale < 0.999:
            new_width = max(2, int(frame.shape[1] * scale))
            new_height = max(2, int(frame.shape[0] * scale))
            working_frame = cv2.resize(frame, (new_width, new_height), interpolation=cv2.INTER_AREA)

        for quality_attempt in quality_attempts:
            ok, encoded = cv2.imencode(
                ".jpg",
                working_frame,
                [int(cv2.IMWRITE_JPEG_QUALITY), quality_attempt],
            )
            if ok and (max_payload_bytes <= 0 or len(encoded) <= max_payload_bytes):
                return encoded.tobytes()

    return None


def measure_encode(frame, frame_count: int, quality: int, max_bytes: int, adaptive: bool) -> tuple[list[float], list[int]]:
    encode_times_ms: list[float] = []
    encoded_sizes: list[int] = []

    for _ in range(WARMUP_FRAMES):
        encode_frame(frame, quality, max_bytes, adaptive)

    for _ in range(frame_count):
        started_at = time.perf_counter()
        encoded = encode_frame(frame, quality, max_bytes, adaptive)
        elapsed_ms = (time.perf_counter() - started_at) * 1000.0
        if encoded is None:
            raise RuntimeError("cv2.imencode failed")

        encode_times_ms.append(elapsed_ms)
        encoded_sizes.append(len(encoded))

    return encode_times_ms, encoded_sizes


def encode_sample(frame, quality: int, max_bytes: int, adaptive: bool) -> bytes:
    encoded = encode_frame(frame, quality, max_bytes, adaptive)
    if encoded is None:
        raise RuntimeError("cv2.imencode failed while writing sample")

    return encoded


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
    print(f"Sample A quality: {BENCHMARK_QUALITY_A}, max bytes: {BENCHMARK_MAX_BYTES_A}, adaptive: {BENCHMARK_ADAPTIVE_A}")
    print(f"Sample B quality: {BENCHMARK_QUALITY_B}, max bytes: {BENCHMARK_MAX_BYTES_B}, adaptive: {BENCHMARK_ADAPTIVE_B}")
    print(f"Warmup frames: {WARMUP_FRAMES}")
    print(f"Measured frames: {BENCHMARK_FRAMES}")
    print(f"Sample output: {BENCHMARK_OUTPUT_DIR}")
    print()

    BENCHMARK_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    sample_a_path = BENCHMARK_OUTPUT_DIR / f"sample_{width_a}x{height_a}_q{BENCHMARK_QUALITY_A}.jpg"
    sample_b_path = BENCHMARK_OUTPUT_DIR / f"sample_{width_b}x{height_b}_q{BENCHMARK_QUALITY_B}.jpg"
    sample_a_path.write_bytes(encode_sample(frame_a, BENCHMARK_QUALITY_A, BENCHMARK_MAX_BYTES_A, BENCHMARK_ADAPTIVE_A))
    sample_b_path.write_bytes(encode_sample(frame_b, BENCHMARK_QUALITY_B, BENCHMARK_MAX_BYTES_B, BENCHMARK_ADAPTIVE_B))
    print(f"Wrote sample A: {sample_a_path}")
    print(f"Wrote sample B: {sample_b_path}")
    print()

    result_a = summarize(
        f"{width_a}x{height_a} q{BENCHMARK_QUALITY_A} encode",
        *measure_encode(frame_a, BENCHMARK_FRAMES, BENCHMARK_QUALITY_A, BENCHMARK_MAX_BYTES_A, BENCHMARK_ADAPTIVE_A))
    print()
    result_b = summarize(
        f"{width_b}x{height_b} q{BENCHMARK_QUALITY_B} encode",
        *measure_encode(frame_b, BENCHMARK_FRAMES, BENCHMARK_QUALITY_B, BENCHMARK_MAX_BYTES_B, BENCHMARK_ADAPTIVE_B))
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
