#!/usr/bin/env python3
"""
Opus-MT 翻译模型下载脚本
用法: python download_model.py
"""

import os
import urllib.request
import ssl

# 忽略SSL证书验证（某些环境下需要）
ssl._create_default_https_context = ssl._create_unverified_context

MODEL_DIR = "Models/opus-mt-en-zh"
# 使用 HuggingFace 官方镜像
BASE_URL = "https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/resolve/main"

# 备用镜像（如果官方下载慢）
# BASE_URL = "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main"

FILES = [
    ("model.onnx", 90),      # 约90MB
    ("vocab.json", 1),       # 约1MB
    ("config.json", 0.001),  # 约1KB
    ("source.spm", 1),       # 约1MB
    ("target.spm", 1),       # 约1MB
]

def download_file(url, path):
    """下载单个文件并显示进度"""
    def progress_hook(count, block_size, total_size):
        if total_size > 0:
            percent = int(count * block_size * 100 / total_size)
            if percent % 10 == 0:
                print(f"\r  进度: {percent}%", end="", flush=True)
    
    urllib.request.urlretrieve(url, path, reporthook=progress_hook)
    print()  # 换行

def main():
    print("=" * 50)
    print("Opus-MT 英译中模型下载工具")
    print("=" * 50)
    
    # 创建目录
    os.makedirs(MODEL_DIR, exist_ok=True)
    print(f"\n模型将保存到: {os.path.abspath(MODEL_DIR)}\n")
    
    for filename, size_mb in FILES:
        filepath = os.path.join(MODEL_DIR, filename)
        url = f"{BASE_URL}/{filename}"
        
        if os.path.exists(filepath):
            actual_size = os.path.getsize(filepath) / (1024 * 1024)
            print(f"✓ {filename} 已存在 ({actual_size:.2f} MB)")
            continue
        
        print(f"\n⬇ 正在下载: {filename} (预计 {size_mb} MB)...")
        try:
            download_file(url, filepath)
            actual_size = os.path.getsize(filepath) / (1024 * 1024)
            print(f"✓ 下载完成: {actual_size:.2f} MB")
        except Exception as e:
            print(f"✗ 下载失败: {e}")
            if os.path.exists(filepath):
                os.remove(filepath)
    
    print("\n" + "=" * 50)
    print("下载完成!")
    print("=" * 50)
    
    # 显示文件列表
    print("\n已下载文件:")
    for filename, _ in FILES:
        filepath = os.path.join(MODEL_DIR, filename)
        if os.path.exists(filepath):
            size = os.path.getsize(filepath) / (1024 * 1024)
            print(f"  {filename}: {size:.2f} MB")

if __name__ == "__main__":
    main()
