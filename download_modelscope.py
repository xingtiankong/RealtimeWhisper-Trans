#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从魔搭社区 (ModelScope) 下载翻译模型
"""

import os
import sys
import urllib.request

# 设置编码
if sys.platform == 'win32':
    import codecs
    sys.stdout = codecs.getwriter('utf-8')(sys.stdout.buffer)

MODEL_DIR = "Models/csanmt-en-zh"

# 模型文件列表
FILES = {
    "tf_graph.pb": "https://modelscope.cn/models/damo/nlp_csanmt_translation_en2zh/resolve/master/tf_graph.pb",
    "spiece.model": "https://modelscope.cn/models/damo/nlp_csanmt_translation_en2zh/resolve/master/spiece.model",
    "vocab.txt": "https://modelscope.cn/models/damo/nlp_csanmt_translation_en2zh/resolve/master/vocab.txt",
    "configuration.json": "https://modelscope.cn/models/damo/nlp_csanmt_translation_en2zh/resolve/master/configuration.json",
}

def download_file(url, path, filename):
    """下载单个文件"""
    if os.path.exists(path):
        size = os.path.getsize(path) / (1024*1024)
        print(f"[OK] {filename} already exists ({size:.1f} MB)")
        return True
    
    print(f"[Downloading] {filename}...", end=" ", flush=True)
    try:
        headers = {'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
        req = urllib.request.Request(url, headers=headers)
        
        with urllib.request.urlopen(req, timeout=300) as response:
            with open(path, 'wb') as f:
                f.write(response.read())
        
        size = os.path.getsize(path) / (1024*1024)
        print(f"Done ({size:.1f} MB)")
        return True
        
    except Exception as e:
        print(f"Failed! {str(e)[:50]}")
        return False

def main():
    print("=" * 60)
    print("ModelScope CSANMT EN-ZH Model Downloader")
    print("=" * 60)
    print(f"\nTarget directory: {os.path.abspath(MODEL_DIR)}\n")
    
    os.makedirs(MODEL_DIR, exist_ok=True)
    
    success_count = 0
    for filename, url in FILES.items():
        path = os.path.join(MODEL_DIR, filename)
        if download_file(url, path, filename):
            success_count += 1
    
    print("\n" + "=" * 60)
    print(f"Downloaded: {success_count}/{len(FILES)} files")
    if success_count == len(FILES):
        print("All files downloaded successfully!")
    else:
        print("Some files failed. Check network or download manually from:")
        print("https://modelscope.cn/models/damo/nlp_csanmt_translation_en2zh/files")
    print("=" * 60)

if __name__ == "__main__":
    main()
