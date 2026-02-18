#!/bin/bash
# 下载 Opus-MT 英译中 ONNX 模型

MODEL_DIR="Models/opus-mt-en-zh"
mkdir -p "$MODEL_DIR"

echo "开始下载 Opus-MT 翻译模型..."

# 使用 hf-mirror 镜像加速
cd "$MODEL_DIR"

# 下载模型文件
curl -L -o model.onnx "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main/model.onnx"

curl -L -o vocab.json "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main/vocab.json"

curl -L -o config.json "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main/config.json"

curl -L -o source.spm "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main/source.spm"

curl -L -o target.spm "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main/target.spm"

echo "下载完成！"
ls -lh
