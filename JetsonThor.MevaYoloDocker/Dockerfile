ARG BASE_IMAGE=ultralytics/ultralytics:latest-nvidia-arm64
FROM ${BASE_IMAGE}

WORKDIR /app

COPY app/stream_meva_yolo.py /app/stream_meva_yolo.py

ENV PYTHONUNBUFFERED=1

CMD ["python3", "/app/stream_meva_yolo.py"]
