cmake_minimum_required(VERSION 3.16)
project(jetson_ollama_bridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_executable(jetson_ollama_bridge
    src/main.cpp
)

target_compile_options(jetson_ollama_bridge PRIVATE
    -Wall
    -Wextra
    -Wpedantic
)
