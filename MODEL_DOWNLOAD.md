# 本地翻译模型下载说明

## 方法1：使用 Argos Translate 模型（推荐）

运行 Python 脚本自动下载：
```bash
python download_argos_model.py
```

或者手动下载：
1. 访问: https://www.argosopentech.com/argospm/index/
2. 搜索 "en_zh" 包
3. 下载 `translate-en_zh-1_9.zip`
4. 解压到 `Models/argos-translate/`

## 方法2：手动下载 ONNX 模型

由于 HuggingFace 访问限制，请使用以下方式：

### 使用 Git LFS 克隆（需要安装 Git LFS）
```bash
git lfs install
git clone https://huggingface.co/Helsinki-NLP/opus-mt-en-zh Models/opus-mt-en-zh
```

### 或者从 ModelScope 下载（国内镜像）
访问: https://www.modelscope.cn/models/Helsinki-NLP/opus-mt-en-zh/files

下载以下文件到 `Models/opus-mt-en-zh/`：
- model.onnx
- vocab.json
- config.json
- source.spm
- target.spm

## 方法3：暂时使用在线翻译

如果不下载本地模型，程序会自动使用 Google 翻译（需要联网）。

## 模型文件大小

- Opus-MT ONNX 模型：约 90MB
- Argos Translate 模型：约 120MB

## 验证安装

运行程序后查看日志：
- 如果显示 "本地翻译模型加载成功"，说明本地模型工作正常
- 如果显示 "本地翻译模型不存在"，将使用在线翻译
