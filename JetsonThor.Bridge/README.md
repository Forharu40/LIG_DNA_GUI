# Jetson Thor Ollama Bridge

This sample listens for a JSON line from the C# control GUI, forwards the prompt to Ollama on the Jetson, and returns a single JSON line response.

## Protocol

Control PC -> Jetson:

```json
{"requestId":"abc123","model":"gemma3","prompt":"Why is the sky blue?"}
```

Jetson -> Control PC:

```json
{"requestId":"abc123","status":"ok","response":"...","error":"","elapsedMs":1487}
```

## Build on Jetson Thor

```bash
cd JetsonThor.Bridge
cmake -S . -B build
cmake --build build -j
```

## Run on Jetson Thor

1. Start Ollama on the Jetson.
2. Make sure the model exists, for example `ollama pull gemma3`.
3. Start the bridge:

```bash
./build/jetson_ollama_bridge --listen 0.0.0.0 --port 7001 --ollama-host 127.0.0.1 --ollama-port 11434
```

## GUI setup

In the WPF app:

1. Set `Host` to the Jetson IP address.
2. Keep `Port` as `7001` unless you changed it.
3. Set `Model` to a model available in Ollama.
4. Press `Load Example` to use the official Ollama introduction prompt.
5. Press `Send To Jetson`.
