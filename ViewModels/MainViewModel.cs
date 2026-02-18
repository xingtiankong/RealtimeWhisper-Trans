using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioTranscriber.Models;
using AudioTranscriber.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AudioTranscriber.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly AudioRecorderService _audioRecorder;
        private readonly WhisperRecognitionService _whisperService;
        private readonly AudioDeviceService _deviceService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource? _recordingCts;

        [ObservableProperty]
        private ObservableCollection<TranscriptSegment> _transcriptSegments = new();

        [ObservableProperty]
        private ObservableCollection<AudioDeviceInfo> _audioDevices = new();

        [ObservableProperty]
        private AudioDeviceInfo? _selectedDevice;

        [ObservableProperty]
        private RecordingState _currentState = RecordingState.Idle;

        [ObservableProperty]
        private string _statusMessage = "准备就绪";

        [ObservableProperty]
        private bool _enableTranslation = true;

        [ObservableProperty]
        private double _audioLevel;

        [ObservableProperty]
        private bool _isModelLoaded;

        [ObservableProperty]
        private string _saveDirectory = "";

        [ObservableProperty]
        private bool _isSystemSoundMode;

        [ObservableProperty]
        private bool _autoSave = true;

        [ObservableProperty]
        private float _modelDownloadProgress;

        [ObservableProperty]
        private bool _isDownloadingModel;

        // 翻译提供者列表
        [ObservableProperty]
        private ObservableCollection<TranslationProvider> _translationProviders = new();

        [ObservableProperty]
        private TranslationProvider? _selectedTranslationProvider;

        // 音频流处理器（生产者-消费者模式）
        private AudioStreamProcessor? _audioProcessor;
        
        // 当前翻译服务
        private ITranslationService? _currentTranslationService;

        public MainViewModel()
        {
            try
            {
                App.LogInfo("初始化 MainViewModel...");
                
                _audioRecorder = new AudioRecorderService();
                _whisperService = new WhisperRecognitionService();
                _deviceService = new AudioDeviceService();
                _settingsService = new SettingsService();

                // 初始化翻译提供者列表
                InitializeTranslationProviders();

                // 加载设置
                LoadSettings();

                // 加载音频设备
                LoadAudioDevices();

                // 订阅事件
                _audioRecorder.AudioDataAvailable += OnAudioDataAvailable;
                _audioRecorder.RecordingError += OnRecordingError;
                _whisperService.SegmentRecognized += OnSegmentRecognized;
                _whisperService.ErrorOccurred += OnWhisperError;
                _whisperService.DownloadProgress += OnModelDownloadProgress;

                // 初始化模型
                _ = InitializeAsync();
                
                App.LogInfo("MainViewModel 初始化完成");
            }
            catch (Exception ex)
            {
                App.LogInfo($"MainViewModel 初始化失败: {ex.Message}");
                StatusMessage = $"初始化失败: {ex.Message}";
            }
        }

        private async void InitializeTranslationProviders()
        {
            TranslationProviders = new ObservableCollection<TranslationProvider>();
            
            // 首先添加非Ollama的选项
            TranslationProviders.Add(new TranslationProvider("local", "📚 本地词典", "内置词典翻译，无需联网", requiresInternet: false, requiresLocalModel: false));
            TranslationProviders.Add(new TranslationProvider("baidu", "🔵 百度翻译", "百度翻译API，需要申请密钥", requiresInternet: true));
            
            // 异步获取Ollama模型列表
            await RefreshOllamaModelsAsync();
            
            // 默认选择第一个可用的（优先Ollama，其次本地词典）
            SelectedTranslationProvider = TranslationProviders.FirstOrDefault(p => p.Id.StartsWith("ollama-")) 
                ?? TranslationProviders.FirstOrDefault(p => p.Id == "local")
                ?? TranslationProviders.First();
            
            // 创建默认翻译服务
            UpdateTranslationService();
        }

        /// <summary>
        /// 刷新Ollama模型列表
        /// </summary>
        [RelayCommand]
        private async Task RefreshOllamaModelsAsync()
        {
            try
            {
                App.LogInfo("正在获取Ollama模型列表...");
                StatusMessage = "正在获取Ollama模型列表...";
                
                // 执行 ollama list 命令
                var models = await GetOllamaModelsAsync();
                
                if (models.Count > 0)
                {
                    App.LogInfo($"发现 {models.Count} 个Ollama模型");
                    
                    // 移除旧的Ollama选项
                    var oldOllamaProviders = TranslationProviders.Where(p => p.Id.StartsWith("ollama-")).ToList();
                    foreach (var old in oldOllamaProviders)
                    {
                        TranslationProviders.Remove(old);
                    }
                    
                    // 添加新的Ollama模型选项（插入到最前面）
                    int insertIndex = 0;
                    foreach (var model in models)
                    {
                        var provider = new TranslationProvider(
                            id: $"ollama-{model.Name}",
                            name: $"🤖 {model.Name}",
                            description: $"本地Ollama模型 | 大小: {model.Size}",
                            requiresLocalModel: true
                        );
                        TranslationProviders.Insert(insertIndex++, provider);
                    }
                    
                    StatusMessage = $"已加载 {models.Count} 个Ollama模型";
                }
                else
                {
                    App.LogInfo("未检测到Ollama模型，请运行: ollama pull <模型名>");
                    StatusMessage = "未检测到Ollama模型";
                }
            }
            catch (Exception ex)
            {
                App.LogInfo($"获取Ollama模型列表失败: {ex.Message}");
                StatusMessage = "Ollama未运行或未安装";
            }
        }

        /// <summary>
        /// 执行 ollama list 命令获取模型列表
        /// </summary>
        private async Task<List<OllamaModelInfo>> GetOllamaModelsAsync()
        {
            var models = new List<OllamaModelInfo>();
            
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null) return models;
            
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"ollama list 失败: {error}");
            }
            
            // 解析输出
            // 格式: NAME                    ID              SIZE      MODIFIED
            //       qwen2.5:3b              3aab63f...      1.9 GB    2 hours ago
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // 跳过表头
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // 分割行（按空格分割，但NAME列可能包含空格）
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var name = parts[0];
                    var size = parts.Length >= 3 ? parts[2] + (parts.Length > 3 ? " " + parts[3] : "") : "unknown";
                    
                    models.Add(new OllamaModelInfo
                    {
                        Name = name,
                        Size = size
                    });
                }
            }
            
            return models;
        }

        /// <summary>
        /// Ollama模型信息
        /// </summary>
        private class OllamaModelInfo
        {
            public string Name { get; set; } = "";
            public string Size { get; set; } = "";
        }

        partial void OnSelectedTranslationProviderChanged(TranslationProvider? value)
        {
            if (value != null)
            {
                App.LogInfo($"切换翻译服务: {value.Name}");
                UpdateTranslationService();
                StatusMessage = $"翻译服务: {value.Name}";
            }
        }

        private void UpdateTranslationService()
        {
            var provider = SelectedTranslationProvider;
            if (provider == null) return;

            switch (provider.Id)
            {
                case "ollama-qwen":
                    _currentTranslationService = new OllamaTranslationService("qwen2.5:3b");
                    break;
                case "ollama-llama":
                    _currentTranslationService = new OllamaTranslationService("llama3.2:3b");
                    break;
                case "ollama-gemma":
                    _currentTranslationService = new OllamaTranslationService("gemma2:2b");
                    break;
                case "local":
                    _currentTranslationService = new LocalSmartTranslationService();
                    break;
                case "baidu":
                    _currentTranslationService = new BaiduTranslationService();
                    break;
                default:
                    _currentTranslationService = new OllamaTranslationService();
                    break;
            }

            // 更新音频处理器中的翻译服务
            if (_audioProcessor != null)
            {
                // 需要重新创建处理器以使用新的翻译服务
                _audioProcessor.SegmentRecognized -= OnProcessorSegmentRecognized;
                _audioProcessor.StatusChanged -= OnProcessorStatusChanged;
                _audioProcessor.Dispose();
                
                _audioProcessor = new AudioStreamProcessor(_whisperService, _currentTranslationService);
                _audioProcessor.SegmentRecognized += OnProcessorSegmentRecognized;
                _audioProcessor.StatusChanged += OnProcessorStatusChanged;
            }
        }

        private void InitializeAudioProcessor()
        {
            if (_audioProcessor != null) return;
            
            _audioProcessor = new AudioStreamProcessor(_whisperService, _currentTranslationService ?? new OllamaTranslationService());
            _audioProcessor.SegmentRecognized += OnProcessorSegmentRecognized;
            _audioProcessor.StatusChanged += OnProcessorStatusChanged;
        }

        private void OnProcessorSegmentRecognized(object? sender, TranscriptSegment segment)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                TranscriptSegments.Add(segment);
                if (TranscriptSegments.Count > 100)
                {
                    TranscriptSegments.RemoveAt(0);
                }
            });
        }

        private void OnProcessorStatusChanged(object? sender, string status)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = status;
            });
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Settings;
            SaveDirectory = settings.SaveDirectory;
            EnableTranslation = settings.EnableTranslation;
            AutoSave = settings.AutoSave;
            IsSystemSoundMode = settings.IsSystemSound;
        }

        private void LoadAudioDevices()
        {
            AudioDevices.Clear();
            
            // 添加麦克风设备
            foreach (var device in _deviceService.GetInputDevices())
            {
                AudioDevices.Add(device);
            }
            
            // 添加系统声音设备
            foreach (var device in _deviceService.GetOutputDevices())
            {
                AudioDevices.Add(device);
            }

            // 选择默认设备
            var settings = _settingsService.Settings;
            SelectedDevice = AudioDevices.FirstOrDefault(d => d.Id == settings.SelectedDeviceId) 
                ?? AudioDevices.FirstOrDefault();
        }

        private async Task InitializeAsync()
        {
            try
            {
                StatusMessage = "正在加载 Whisper 模型...";
                await _whisperService.InitializeAsync();
                IsModelLoaded = true;
                StatusMessage = "模型加载完成，可以开始录音";
            }
            catch (Exception ex)
            {
                StatusMessage = $"模型加载失败: {ex.Message}";
                IsModelLoaded = false;
            }
        }

        [RelayCommand]
        private async Task StartRecording()
        {
            App.LogInfo("StartRecording 被调用");
            
            if (!IsModelLoaded)
            {
                StatusMessage = "请等待模型加载完成";
                App.LogInfo("模型未加载，无法开始录音");
                return;
            }

            if (SelectedDevice == null)
            {
                StatusMessage = "请先选择音频设备";
                App.LogInfo("未选择音频设备");
                return;
            }

            try
            {
                _recordingCts = new CancellationTokenSource();
                
                // 初始化音频处理器
                InitializeAudioProcessor();
                
                // 获取实际采样率
                int sampleRate = 48000; // 默认
                if (_audioRecorder.CurrentWaveFormat != null)
                {
                    sampleRate = _audioRecorder.CurrentWaveFormat.SampleRate;
                    App.LogInfo($"检测到音频采样率: {sampleRate}Hz");
                }
                
                _audioProcessor?.StartProcessing(_recordingCts.Token, sampleRate);
                
                App.LogInfo($"开始录音，设备: {SelectedDevice.Name}, IsLoopback: {SelectedDevice.IsLoopback}");
                
                if (SelectedDevice.IsLoopback)
                {
                    // 捕获系统声音
                    _audioRecorder.StartRecordingSystemSound(SelectedDevice.Id);
                    IsSystemSoundMode = true;
                    StatusMessage = "正在捕获系统声音...";
                }
                else
                {
                    // 录制麦克风
                    if (int.TryParse(SelectedDevice.Id, out int deviceNumber))
                    {
                        _audioRecorder.StartRecordingMicrophone(deviceNumber);
                        IsSystemSoundMode = false;
                        StatusMessage = "正在录音...";
                    }
                    else
                    {
                        throw new InvalidOperationException($"无效的设备ID: {SelectedDevice.Id}");
                    }
                }
                
                CurrentState = RecordingState.Recording;
                App.LogInfo("录音已开始");
            }
            catch (Exception ex)
            {
                StatusMessage = $"开始录音失败: {ex.Message}";
                CurrentState = RecordingState.Error;
                App.LogInfo($"开始录音失败: {ex.Message}");
                
                // 显示详细错误信息
                System.Windows.MessageBox.Show(
                    $"开始录音失败:\n{ex.Message}\n\n{ex.StackTrace}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task StopRecording()
        {
            _audioRecorder.StopRecording();
            _recordingCts?.Cancel();
            
            // 停止音频处理器
            _audioProcessor?.StopProcessing();
            
            CurrentState = RecordingState.Idle;
            StatusMessage = "录音已停止";

            // 如果启用了自动保存，保存当前字幕
            if (AutoSave && TranscriptSegments.Count > 0)
            {
                await AutoSaveTranscript();
            }
        }

        [RelayCommand]
        private void ClearTranscript()
        {
            TranscriptSegments.Clear();
            StatusMessage = "字幕已清空";
        }

        [RelayCommand]
        private async Task SaveTranscript()
        {
            if (TranscriptSegments.Count == 0)
            {
                StatusMessage = "没有内容可保存";
                return;
            }

            try
            {
                _settingsService.EnsureSaveDirectoryExists();
                
                var fileName = $"transcript_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(SaveDirectory, fileName);

                var lines = TranscriptSegments.Select(s => 
                    $"[{s.StartTime:hh\\:mm\\:ss}] {s.OriginalText}" +
                    (s.IsEnglish && !string.IsNullOrEmpty(s.TranslatedText) 
                        ? $"\n[翻译] {s.TranslatedText}" 
                        : ""));
                
                await File.WriteAllLinesAsync(filePath, lines);
                StatusMessage = $"已保存到: {filePath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        private async Task AutoSaveTranscript()
        {
            try
            {
                _settingsService.EnsureSaveDirectoryExists();
                
                var fileName = $"transcript_auto_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(SaveDirectory, fileName);

                var lines = TranscriptSegments.Select(s => 
                    $"[{s.StartTime:hh\\:mm\\:ss}] {s.OriginalText}" +
                    (s.IsEnglish && !string.IsNullOrEmpty(s.TranslatedText) 
                        ? $"\n[翻译] {s.TranslatedText}" 
                        : ""));
                
                await File.WriteAllLinesAsync(filePath, lines);
                StatusMessage = $"自动保存: {filePath}";
            }
            catch { }
        }

        [RelayCommand]
        private void ToggleTranslation()
        {
            EnableTranslation = !EnableTranslation;
            _settingsService.Settings.EnableTranslation = EnableTranslation;
            _settingsService.SaveSettings();
            StatusMessage = EnableTranslation ? "翻译已启用" : "翻译已禁用";
        }

        [RelayCommand]
        private void ChangeSaveDirectory()
        {
            try
            {
                App.LogInfo("打开文件夹选择对话框...");
                
                // 使用 WPF 的 OpenFolderDialog (Windows 10 1803+)
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择字幕保存文件夹",
                    CheckFileExists = false,
                    CheckPathExists = true,
                    FileName = "选择文件夹"
                };

                if (Directory.Exists(SaveDirectory))
                {
                    dialog.InitialDirectory = SaveDirectory;
                }

                if (dialog.ShowDialog() == true)
                {
                    // 获取选择的文件夹路径
                    var selectedPath = Path.GetDirectoryName(dialog.FileName);
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        SaveDirectory = selectedPath;
                        _settingsService.Settings.SaveDirectory = SaveDirectory;
                        _settingsService.SaveSettings();
                        StatusMessage = $"保存位置: {SaveDirectory}";
                        App.LogInfo($"保存位置已更改: {SaveDirectory}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"选择文件夹失败: {ex.Message}";
                App.LogInfo($"选择文件夹失败: {ex.Message}");
                
                // 备选方案：直接设置默认路径
                var defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AudioTranscriber");
                
                if (!Directory.Exists(defaultPath))
                    Directory.CreateDirectory(defaultPath);
                
                SaveDirectory = defaultPath;
                _settingsService.Settings.SaveDirectory = SaveDirectory;
                _settingsService.SaveSettings();
                
                System.Windows.MessageBox.Show(
                    $"选择文件夹对话框出错，已使用默认路径:\n{defaultPath}\n\n错误: {ex.Message}",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }

        partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
        {
            if (value != null)
            {
                _settingsService.Settings.SelectedDeviceId = value.Id;
                _settingsService.Settings.IsSystemSound = value.IsLoopback;
                _settingsService.SaveSettings();
                StatusMessage = $"已选择: {value.Name}";
            }
        }

        private async void OnAudioDataAvailable(object? sender, byte[] audioData)
        {
            try
            {
                // 计算音频电平
                AudioLevel = CalculateAudioLevel(audioData);

                // 提交到音频处理器（生产者-消费者模式）
                _audioProcessor?.AddAudioData(audioData);
            }
            catch (Exception ex)
            {
                App.LogInfo($"处理音频数据失败: {ex.Message}");
            }
        }

        private async void OnSegmentRecognized(object? sender, TranscriptSegment segment)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // 检查是否是重复或相似的片段（Whisper 会先给初步结果，再给完整结果）
                var existingSegment = FindSimilarSegment(segment);
                
                if (existingSegment != null)
                {
                    // 如果新片段更长或更完整，更新现有片段
                    if (segment.OriginalText.Length > existingSegment.OriginalText.Length)
                    {
                        existingSegment.OriginalText = segment.OriginalText;
                        existingSegment.EndTimeSeconds = segment.EndTimeSeconds;
                        
                        // 重新翻译
                        if (segment.IsEnglish && EnableTranslation)
                        {
                            try
                            {
                                existingSegment.TranslatedText = await _currentTranslationService!.TranslateAsync(
                                    segment.OriginalText, "en", "zh");
                            }
                            catch
                            {
                                existingSegment.TranslatedText = "[翻译失败]";
                            }
                        }
                        
                        App.LogInfo($"更新片段: '{existingSegment.OriginalText}'");
                        StatusMessage = $"更新: {existingSegment.OriginalText}";
                    }
                    else
                    {
                        // 忽略较短的重复结果
                        App.LogInfo($"忽略重复片段: '{segment.OriginalText}'");
                    }
                    return;
                }

                // 如果是英文且启用了翻译，进行翻译
                if (segment.IsEnglish && EnableTranslation)
                {
                    try
                    {
                        segment.TranslatedText = await _currentTranslationService!.TranslateAsync(
                            segment.OriginalText, "en", "zh");
                    }
                    catch
                    {
                        segment.TranslatedText = "[翻译失败]";
                    }
                }

                TranscriptSegments.Add(segment);
                
                // 限制最大数量，防止内存溢出
                if (TranscriptSegments.Count > 100)
                {
                    TranscriptSegments.RemoveAt(0);
                }

                // 自动保存（可选）
                if (AutoSave && TranscriptSegments.Count % 10 == 0)
                {
                    await AutoSaveTranscript();
                }

                StatusMessage = $"识别: {segment.OriginalText}";
            });
        }

        /// <summary>
        /// 查找相似的现有片段（用于合并重复输出）
        /// </summary>
        private TranscriptSegment? FindSimilarSegment(TranscriptSegment newSegment)
        {
            if (TranscriptSegments.Count == 0)
                return null;

            var newText = newSegment.OriginalText.ToLower().Trim();
            var newTime = newSegment.StartTimeSeconds;

            // 从后往前查找最近的片段
            for (int i = TranscriptSegments.Count - 1; i >= Math.Max(0, TranscriptSegments.Count - 5); i--)
            {
                var existing = TranscriptSegments[i];
                var existingText = existing.OriginalText.ToLower().Trim();
                
                // 时间差在 3 秒内
                var timeDiff = Math.Abs(existing.StartTimeSeconds - newTime);
                if (timeDiff > 3)
                    continue;

                // 检查是否是包含关系或相似
                if (existingText.Contains(newText) || newText.Contains(existingText) ||
                    CalculateSimilarity(existingText, newText) > 0.6)
                {
                    return existing;
                }
            }

            return null;
        }

        /// <summary>
        /// 计算两个字符串的相似度（简单的最长公共子序列）
        /// </summary>
        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            var longer = s1.Length > s2.Length ? s1 : s2;
            var shorter = s1.Length > s2.Length ? s2 : s1;

            int maxLength = longer.Length;
            if (maxLength == 0)
                return 1.0;

            // 简单的字符匹配计数
            int matchCount = 0;
            var shorterChars = shorter.ToCharArray();
            var longerChars = longer.ToCharArray();

            foreach (var c in shorterChars)
            {
                if (longerChars.Contains(c))
                {
                    matchCount++;
                }
            }

            return (double)matchCount / maxLength;
        }

        private void OnRecordingError(object? sender, Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"录音错误: {ex.Message}";
                CurrentState = RecordingState.Error;
            });
        }

        private void OnWhisperError(object? sender, string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = message;
            });
        }

        private void OnModelDownloadProgress(object? sender, float progress)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ModelDownloadProgress = progress;
                IsDownloadingModel = progress < 100;
                StatusMessage = $"正在下载模型... {progress:F1}%";
            });
        }

        private double CalculateAudioLevel(byte[] audioData)
        {
            if (audioData == null || audioData.Length < 2)
                return 0;

            try
            {
                double sum = 0;
                int sampleCount = 0;
                
                for (int i = 0; i < audioData.Length - 1; i += 2)
                {
                    // 转换为 short (16-bit signed)
                    short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
                    
                    // 转换为 int 后再取绝对值，避免 short.MinValue (-32768) 溢出
                    int sampleValue = sample;
                    if (sampleValue < 0)
                        sampleValue = -sampleValue;
                    
                    sum += sampleValue;
                    sampleCount++;
                }

                if (sampleCount == 0)
                    return 0;

                // 计算平均电平并归一化到 0-100
                double average = sum / sampleCount;
                double level = (average / 327.68);
                
                return Math.Min(100, Math.Max(0, level));
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            _recordingCts?.Cancel();
            _recordingCts?.Dispose();
            _audioRecorder?.Dispose();
            _whisperService?.Dispose();
            (_currentTranslationService as IDisposable)?.Dispose();
        }
    }
}
