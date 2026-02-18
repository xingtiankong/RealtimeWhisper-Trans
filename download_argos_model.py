#!/usr/bin/env python3
"""
使用 Argos Translate 本地模型
更轻量、更容易下载
"""

import os
import urllib.request
import zipfile

# Argos Translate 模型
# 英文 -> 中文
MODEL_URL = "https://argosopentech.nyc3.digitaloceanspaces.com/argos-translate/packages/v1/translate-en_zh-1_9.zip"
MODEL_DIR = "Models/argos-translate"

def download_and_extract():
    os.makedirs(MODEL_DIR, exist_ok=True)
    
    zip_path = os.path.join(MODEL_DIR, "model.zip")
    
    # 检查是否已存在
    if os.path.exists(os.path.join(MODEL_DIR, "model")):
        print("模型已存在！")
        return
    
    print("正在下载 Argos Translate 英译中模型...")
    print(f"URL: {MODEL_URL}")
    
    def progress_hook(count, block_size, total_size):
        if total_size > 0:
            percent = min(100, int(count * block_size * 100 / total_size))
            if count % 10 == 0:
                print(f"\r下载进度: {percent}%", end="", flush=True)
    
    try:
        urllib.request.urlretrieve(MODEL_URL, zip_path, reporthook=progress_hook)
        print("\n下载完成，正在解压...")
        
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall(MODEL_DIR)
        
        os.remove(zip_path)
        print("✓ 模型安装完成！")
        
    except Exception as e:
        print(f"\n✗ 错误: {e}")
        print("\n请手动下载：")
        print(MODEL_URL)
        print(f"下载后解压到: {os.path.abspath(MODEL_DIR)}")

if __name__ == "__main__":
    download_and_extract()
