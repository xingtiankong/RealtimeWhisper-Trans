# 🎙️ 语音转写姬 - Audio Transcriber

一款二次元风格的桌面音频转写与翻译软件，支持**麦克风**和**系统声音**实时识别，并自动将英文翻译成中文。

## ✨ 新特性

### 🎧 音频源选择
- **麦克风** - 录制外部声音（说话、会议等）
- **系统声音** - 捕获电脑播放的音频（视频、游戏、在线会议等）
- **设备切换** - 一键切换不同音频源

### 📁 自定义存储
- **指定保存文件夹** - 选择字幕文件的存储位置
- **自动保存** - 录音停止后自动保存字幕
- **自动命名** - 按时间戳自动命名文件

### 🌐 实时翻译
- **自动识别英文** - 检测到英文时自动翻译为中文
- **双语字幕** - 原文在上，翻译在下
- **翻译开关** - 可自由开启/关闭翻译功能

## 📋 系统要求

- Windows 10/11 (64位)
- .NET 9.0 运行时
- 麦克风（可选，用于录制外部声音）
- 至少 2GB 可用内存
- 约 150MB 磁盘空间（用于下载模型）

## 🚀 快速开始

### 1. 运行程序

```bash
cd X:\AI_project\CSharpProjects\AudioTranscriber
dotnet run
```

### 2. 选择音频源

点击 **🎧 音频源** 下拉框选择：
- **麦克风** - 用于录制你的声音
- **🖥️ 系统声音** - 用于捕获电脑播放的声音（如视频、音乐）

### 3. 设置保存位置

点击 **更改** 按钮选择字幕文件的保存文件夹。

### 4. 开始录音

点击 **🔴 红色按钮** 或按 **空格键** 开始录音/捕获。

### 5. 查看字幕

- 识别结果实时显示在下方区域
- 英文内容会自动显示中文翻译
- 翻译显示在粉色背景区域

### 6. 保存字幕

- **自动保存** - 停止录音后自动保存
- **手动保存** - 点击 **💾 保存** 按钮

## 🎮 快捷键

| 快捷键 | 功能 |
|--------|------|
| `空格` | 开始/停止录音 |
| `Ctrl + S` | 保存当前字幕 |

## 📝 字幕文件格式

```
[00:00:05] Hello everyone
[翻译] 大家好

[00:00:08] Welcome to the meeting
[翻译] 欢迎参加会议
```

## 📁 项目结构

```
AudioTranscriber/
├── Models/
│   └── TranscriptSegment.cs       # 字幕片段模型
├── ViewModels/
│   └── MainViewModel.cs           # 主视图模型
├── Services/
│   ├── AudioRecorderService.cs    # 音频录制（麦克风+系统声音）
│   ├── AudioDeviceService.cs      # 音频设备管理
│   ├── WhisperRecognitionService.cs  # Whisper语音识别
│   ├── TranslationService.cs      # 翻译服务
│   └── SettingsService.cs         # 设置管理（保存路径等）
├── Converters/
│   └── Converters.cs              # 数据转换器
├── MainWindow.xaml                # 主窗口界面（二次元风格）
├── MainWindow.xaml.cs             # 主窗口代码
└── App.xaml                       # 应用资源
```

## 🔧 高级配置

### 配置文件位置

设置保存在：
```
%AppData%\AudioTranscriber\settings.json
```

可配置项：
```json
{
  "SaveDirectory": "C:\\Users\\XXX\\Documents\\AudioTranscriber",
  "SelectedDeviceId": "0",
  "IsSystemSound": false,
  "EnableTranslation": true,
  "AutoSave": false
}
```

### 更换 Whisper 模型

默认使用 `base` 模型，平衡速度和准确度。可以在 `WhisperRecognitionService.cs` 中更换：

- `Tiny` - 最快，准确度较低
- `Base` - 默认，平衡选择
- `Small` - 较慢，更准确
- `Medium` - 很慢，非常准确
- `Large` - 最慢，最准确

## 🎨 自定义主题

可以在 `MainWindow.xaml` 中修改颜色：

- 主背景色：`#FF1A1A2E` (深蓝色)
- 强调色：`#FFFF6B9D` (粉色渐变)
- 成功色：`#FF00D4AA` (青色)

## 📝 更新日志

### v1.1.0
- ✅ 支持系统声音捕获（WASAPI Loopback）
- ✅ 音频设备选择功能
- ✅ 自定义字幕保存文件夹
- ✅ 自动保存功能
- ✅ 改进的UI布局

### v1.0.0
- ✅ 初始版本发布
- ✅ 支持麦克风录音
- ✅ 实时语音识别
- ✅ 英文自动翻译中文
- ✅ 二次元风格UI

## 📄 许可证

MIT License

## 💖 致谢

- [Whisper.net](https://github.com/sandrohanea/whisper.net) - 语音识别
- [NAudio](https://github.com/naudio/NAudio) - 音频处理
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM 框架

---

Made with 💖 for anime lovers~ 🌸
