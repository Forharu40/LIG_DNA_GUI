ARG BASE_IMAGE=ultralytics/ultralytics:latest-nvidia-arm64
FROM ${BASE_IMAGE}

ARG MODEL_NAME=yolo11s.pt

WORKDIR /app

COPY app/stream_live_camera_yolo.py /app/stream_live_camera_yolo.py

RUN pip install --no-cache-dir "lap>=0.5.12"

RUN mkdir -p /opt/models \
    && python3 -c "from ultralytics import YOLO; YOLO('${MODEL_NAME}')" \
    && cp -f ${MODEL_NAME} /opt/models/${MODEL_NAME}

ENV PYTHONUNBUFFERED=1 \
    MODEL_PATH=/opt/models/${MODEL_NAME}

CMD ["python3", "/app/stream_live_camera_yolo.py"]
