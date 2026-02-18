using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioTranscriber.Services;

namespace AudioTranscriber.Models
{
    /// <summary>
    /// 音频流处理器 - 使用生产者消费者模式
    /// </summary>
    public class AudioStreamProcessor : IDisposable
    {
        private readonly WhisperRecognitionService _whisperService;
        private readonly ITranslationService _translationService;
        private readonly BlockingCollection<byte[]> _audioQueue;
        private readonly ConcurrentQueue<TranscriptSegment> _resultQueue;
        private CancellationTokenSource? _processingCts;
        private Task? _processingTask;
        
        // VAD (语音活动检测) 参数 - 调整为积累更多音频
        private readonly List<byte> _currentBuffer = new();
        private readonly object _bufferLock = new();
        private DateTime _lastVoiceActivity = DateTime.MinValue;
        private DateTime _lastProcessTime = DateTime.MinValue;
        private bool _isSpeaking = false;
        private int _inputSampleRate = 48000; // 动态检测输入采样率
        private const int OutputSampleRate = 16000; // Whisper 需要 16kHz
        private const int SilenceThreshold = 200; // 静音阈值
        private const int MinSpeechDurationMs = 3000; // 最短语音时长3秒（Whisper需要足够时长）
        private const int MaxSpeechDurationMs = 10000; // 最长语音时长10秒
        private const int SilenceTimeoutMs = 1000; // 静音超时1秒
        private const int ForceProcessIntervalMs = 8000; // 强制处理间隔（8秒必出结果）

        public event EventHandler<TranscriptSegment>? SegmentRecognized;
        public event EventHandler<string>? StatusChanged;

        public bool IsProcessing => _processingTask != null && !_processingTask.IsCompleted;

        public AudioStreamProcessor(
            WhisperRecognitionService whisperService,
            ITranslationService translationService)
        {
            _whisperService = whisperService;
            _translationService = translationService;
            _audioQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
            _resultQueue = new ConcurrentQueue<TranscriptSegment>();
        }

        public void StartProcessing(CancellationToken cancellationToken, int inputSampleRate = 48000)
        {
            if (IsProcessing) return;

            _inputSampleRate = inputSampleRate;
            App.LogInfo($"音频处理器启动，输入采样率: {inputSampleRate}Hz");

            _processingCts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessAudioLoop(_processingCts.Token));
            
            // 启动结果处理线程
            _ = Task.Run(() => ProcessResultsLoop(_processingCts.Token));
            
            // 监听外部取消信号
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 外部请求取消，立即停止添加新数据，但让队列中的数据继续处理
                    App.LogInfo("收到停止信号，处理完队列中的数据后停止...");
                    StopAddingNewData();
                }
            });
        }
        
        private void StopAddingNewData()
        {
            try
            {
                _audioQueue.CompleteAdding();
                App.LogInfo("已停止添加新数据，等待队列处理完成...");
            }
            catch (Exception ex)
            {
                App.LogInfo($"停止添加数据时出错: {ex.Message}");
            }
        }

        public void StopProcessing()
        {
            try
            {
                // 首先停止添加新数据
                _audioQueue.CompleteAdding();
                App.LogInfo("已标记队列完成，等待现有数据处理...");
                
                // 延迟取消CTS，让进行中的识别有时间完成
                _ = Task.Run(async () =>
                {
                    await Task.Delay(8000); // 等待8秒，给识别足够时间
                    _processingCts?.Cancel();
                    App.LogInfo("处理器已完全停止");
                });
            }
            catch (Exception ex)
            {
                App.LogInfo($"停止处理时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加音频数据（生产者）
        /// </summary>
        public void AddAudioData(byte[] audioData)
        {
            if (_audioQueue.IsAddingCompleted) return;
            
            try
            {
                // 使用VAD检测语音活动
                var (hasVoice, energy) = DetectVoiceActivity(audioData);
                
                // 每5秒输出一次音频状态日志
                if ((DateTime.Now - _lastProcessTime).TotalSeconds >= 5)
                {
                    App.LogInfo($"音频输入: {audioData.Length} bytes, 能量: {energy:F0}, 有声: {hasVoice}, 缓冲: {_currentBuffer.Count} bytes");
                }
                
                lock (_bufferLock)
                {
                    _currentBuffer.AddRange(audioData);
                    
                    if (hasVoice)
                    {
                        _lastVoiceActivity = DateTime.Now;
                        _isSpeaking = true;
                        StatusChanged?.Invoke(this, "检测到声音...");
                    }
                    
                    // 检查是否需要触发识别
                    if (ShouldTriggerRecognition())
                    {
                        var audioToProcess = _currentBuffer.ToArray();
                        _currentBuffer.Clear();
                        _isSpeaking = false;
                        
                        App.LogInfo($"触发识别，缓冲区: {audioToProcess.Length} bytes");
                        
                        // 提交到队列
                        _audioQueue.Add(audioToProcess);
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogInfo($"AddAudioData 错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 语音活动检测 (VAD) - 处理32位浮点立体声音频
        /// </summary>
        private (bool hasVoice, double energy) DetectVoiceActivity(byte[] audioData)
        {
            if (audioData == null || audioData.Length < 8)
                return (false, 0);

            double sum = 0;
            int sampleCount = 0;
            float maxAmp = 0;
            
            // 读取32位浮点立体声数据（每个样本8字节）
            for (int i = 0; i <= audioData.Length - 8; i += 8)
            {
                float left = BitConverter.ToSingle(audioData, i);
                float right = BitConverter.ToSingle(audioData, i + 4);
                float mono = (left + right) / 2.0f;
                
                maxAmp = Math.Max(maxAmp, Math.Abs(mono));
                sum += mono * mono;
                sampleCount++;
            }

            double energy = sampleCount > 0 ? sum / sampleCount : 0;
            // 浮点音频能量通常在0-1范围，转换为类似16bit的量级便于阈值比较
            energy = energy * 10000;
            
            // 阈值判断
            bool hasVoice = energy > SilenceThreshold || maxAmp > 0.01f;
            
            return (hasVoice, energy);
        }

        private bool ShouldTriggerRecognition()
        {
            // 原始音频是32位浮点立体声（每个样本8字节），采样率48000
            var bufferDurationMs = (double)_currentBuffer.Count / 8 / _inputSampleRate * 1000;
            var silenceDuration = (DateTime.Now - _lastVoiceActivity).TotalMilliseconds;
            var timeSinceLastProcess = (DateTime.Now - _lastProcessTime).TotalMilliseconds;
            
            // 每5秒输出一次调试信息
            if ((DateTime.Now - _lastProcessTime).TotalSeconds >= 5)
            {
                App.LogInfo($"缓冲区状态: {_currentBuffer.Count} bytes, 时长: {bufferDurationMs:F0}ms, 有声: {_isSpeaking}, 静音: {silenceDuration:F0}ms");
            }
            
            // 条件1: 超过最大时长，强制识别
            if (bufferDurationMs >= MaxSpeechDurationMs)
            {
                _lastProcessTime = DateTime.Now;
                return true;
            }
            
            // 条件2: 检测到语音后，静音超过阈值，认为句子结束
            if (_isSpeaking && bufferDurationMs >= MinSpeechDurationMs && silenceDuration >= SilenceTimeoutMs)
            {
                _lastProcessTime = DateTime.Now;
                return true;
            }
            
            // 条件3: 强制定期处理（确保至少3秒出一次结果）
            if (bufferDurationMs >= MinSpeechDurationMs && timeSinceLastProcess >= ForceProcessIntervalMs)
            {
                _lastProcessTime = DateTime.Now;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 音频处理循环（消费者）
        /// </summary>
        private async Task ProcessAudioLoop(CancellationToken cancellationToken)
        {
            await Task.Yield(); // 确保在线程池线程上运行
            
            try
            {
                // 使用 GetConsumingEnumerable 但不传递 cancellationToken
                // 这样即使外部取消，队列中的数据仍然会被处理完
                foreach (var audioData in _audioQueue.GetConsumingEnumerable())
                {
                    if (cancellationToken.IsCancellationRequested && _audioQueue.IsAddingCompleted && _audioQueue.Count == 0)
                    {
                        App.LogInfo("队列已空且已停止添加，退出处理循环");
                        break;
                    }

                    // 使用线程池并行处理
                    var audioDataCopy = audioData;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 使用独立的CTS，给识别30秒时间（首次加载模型可能较慢）
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            await ProcessSingleAudioChunk(audioDataCopy, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            App.LogInfo($"识别任务异常: {ex.GetType().Name}: {ex.Message}");
                            StatusChanged?.Invoke(this, $"识别错误: {ex.Message}");
                        }
                    });
                }
                
                App.LogInfo("音频处理循环已正常退出（队列已处理完毕）");
            }
            catch (OperationCanceledException) 
            {
                App.LogInfo("音频处理循环被取消");
            }
            catch (Exception ex)
            {
                App.LogInfo($"处理循环错误: {ex.Message}");
                StatusChanged?.Invoke(this, $"处理循环错误: {ex.Message}");
            }
        }

        private async Task ProcessSingleAudioChunk(byte[] audioData, CancellationToken cancellationToken)
        {
            if (audioData.Length < 3200) return; // 太短不处理
            
            // 检查模型是否初始化
            if (!_whisperService.IsInitialized)
            {
                App.LogInfo("错误: Whisper模型未初始化，尝试初始化...");
                StatusChanged?.Invoke(this, "正在初始化模型...");
                try
                {
                    await _whisperService.InitializeAsync(cancellationToken);
                    App.LogInfo("模型初始化成功");
                }
                catch (Exception initEx)
                {
                    App.LogInfo($"模型初始化失败: {initEx.Message}");
                    StatusChanged?.Invoke(this, $"模型初始化失败: {initEx.Message}");
                    return;
                }
            }
            
            try
            {
                StatusChanged?.Invoke(this, $"正在识别... ({audioData.Length} bytes)");
                App.LogInfo($"处理音频块: {audioData.Length} bytes, 输入采样率: {_inputSampleRate}Hz");
                
                // 直接使用NAudio进行格式转换（更可靠）
                byte[] wavData;
                try
                {
                    wavData = ConvertRawToWav(audioData, _inputSampleRate);
                    App.LogInfo($"NAudio转换完成: {wavData.Length} bytes");
                    
                    // 保存测试文件（调试用）
                    try
                    {
                        var testPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "AudioTranscriber", "test.wav");
                        File.WriteAllBytes(testPath, wavData);
                        App.LogInfo($"测试音频已保存: {testPath}");
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    App.LogInfo($"NAudio转换失败: {ex.Message}，使用备用方案");
                    // 备用：简单假设输入是浮点，直接转换为16bit整数
                    wavData = SimpleConvertToWav(audioData, _inputSampleRate);
                    App.LogInfo($"备用转换完成: {wavData.Length} bytes");
                }
                
                // 检查取消令牌
                if (cancellationToken.IsCancellationRequested)
                {
                    App.LogInfo("识别被取消（取消令牌）");
                    return;
                }
                
                // 调用Whisper识别
                App.LogInfo("开始调用Whisper识别...");
                var text = await _whisperService.TranscribeAsync(wavData, cancellationToken);
                App.LogInfo($"识别结果: '{text}'");
                
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var segment = new TranscriptSegment
                    {
                        OriginalText = text.Trim(),
                        Language = "auto",
                        StartTimeSeconds = (long)DateTime.Now.TimeOfDay.TotalSeconds,
                        EndTimeSeconds = (long)DateTime.Now.TimeOfDay.TotalSeconds + 1,
                        IsEnglish = IsEnglishText(text)
                    };

                    // 添加到结果队列
                    _resultQueue.Enqueue(segment);
                    App.LogInfo($"已添加到结果队列: '{text.Trim()}'");
                }
                else
                {
                    App.LogInfo("识别结果为空");
                }
            }
            catch (OperationCanceledException)
            {
                App.LogInfo("识别被取消/超时");
                StatusChanged?.Invoke(this, "识别超时，继续监听...");
            }
            catch (Exception ex)
            {
                App.LogInfo($"处理音频块失败: {ex.GetType().Name}: {ex.Message}");
                StatusChanged?.Invoke(this, $"识别失败: {ex.Message}");
            }
        }

        private byte[] ConvertRawToWav(byte[] rawData, int sampleRate)
        {
            // 使用NAudio进行正确的格式转换
            // 假设输入是IEEE浮点立体声，输出16kHz 16bit单声道WAV
            
            using var inputStream = new System.IO.MemoryStream(rawData);
            using var reader = new System.IO.BinaryReader(inputStream);
            
            // 读取浮点数据并转换为16bit整数
            var samples = new List<short>();
            float maxSample = 0;
            int zeroCount = 0;
            
            while (inputStream.Position < inputStream.Length - 7) // 7 = 4 bytes float * 2 channels - 1
            {
                // 读取左右声道（浮点）
                float left = reader.ReadSingle();
                float right = reader.ReadSingle();
                
                // 混合为单声道
                float mono = (left + right) / 2.0f;
                
                // 统计
                maxSample = Math.Max(maxSample, Math.Abs(mono));
                if (Math.Abs(mono) < 0.001f) zeroCount++;
                
                // 限制范围
                mono = Math.Max(-1.0f, Math.Min(1.0f, mono));
                
                // 转换为16bit整数
                short sample = (short)(mono * 32767f);
                samples.Add(sample);
            }
            
            App.LogInfo($"音频统计: 样本数={samples.Count}, 最大振幅={maxSample:F4}, 静音样本={zeroCount}");
            
            // 重采样到16kHz（简单抽值）
            int ratio = sampleRate / 16000;
            if (ratio < 1) ratio = 1;
            
            var resampled = new List<short>();
            for (int i = 0; i < samples.Count; i += ratio)
            {
                resampled.Add(samples[i]);
            }
            
            // 转换为字节数组
            byte[] pcmData = new byte[resampled.Count * 2];
            for (int i = 0; i < resampled.Count; i++)
            {
                pcmData[i * 2] = (byte)(resampled[i] & 0xFF);
                pcmData[i * 2 + 1] = (byte)((resampled[i] >> 8) & 0xFF);
            }
            
            App.LogInfo($"重采样后: {resampled.Count} 样本 ({resampled.Count / 16000.0:F2}秒)");
            
            // 添加WAV头
            return ConvertToWavFormat(pcmData, 16000, 1, 16);
        }

        private byte[] SimpleConvertToWav(byte[] rawData, int sampleRate)
        {
            // 备用方案：假设输入已经是16bit整数，只是需要包装成WAV
            return ConvertToWavFormat(rawData, sampleRate, 1, 16);
        }

        private byte[] ResampleAudioInt16(byte[] input, int inputRate, int outputRate)
        {
            // 直接按16位整数处理（没有浮点转换）
            byte[] monoInput = ConvertStereoToMono(input);
            
            if (inputRate == outputRate) return monoInput;
            
            double ratio = (double)outputRate / inputRate;
            int outputLength = (int)(monoInput.Length * ratio);
            byte[] output = new byte[outputLength];
            
            for (int i = 0; i < outputLength / 2; i++)
            {
                double srcIndex = i / ratio;
                int srcIndexInt = (int)srcIndex;
                double frac = srcIndex - srcIndexInt;
                
                if (srcIndexInt >= monoInput.Length / 2 - 1)
                {
                    srcIndexInt = monoInput.Length / 2 - 2;
                    frac = 1;
                }
                
                short sample1 = (short)(monoInput[srcIndexInt * 2] | (monoInput[srcIndexInt * 2 + 1] << 8));
                short sample2 = (short)(monoInput[(srcIndexInt + 1) * 2] | (monoInput[(srcIndexInt + 1) * 2 + 1] << 8));
                short sample = (short)(sample1 * (1 - frac) + sample2 * frac);
                
                output[i * 2] = (byte)(sample & 0xFF);
                output[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            
            return output;
        }

        private byte[] ResampleAudio(byte[] input, int inputRate, int outputRate)
        {
            // WASAPI 系统声音可能是浮点格式，先转换为16位整数
            byte[] pcm16Input = ConvertFloatToInt16(input);
            
            // 转换为单声道
            byte[] monoInput = ConvertStereoToMono(pcm16Input);
            
            // 简单线性重采样
            if (inputRate == outputRate) return monoInput;
            
            double ratio = (double)outputRate / inputRate;
            int outputLength = (int)(monoInput.Length * ratio);
            byte[] output = new byte[outputLength];
            
            for (int i = 0; i < outputLength / 2; i++)
            {
                double srcIndex = i / ratio;
                int srcIndexInt = (int)srcIndex;
                double frac = srcIndex - srcIndexInt;
                
                if (srcIndexInt >= monoInput.Length / 2 - 1)
                {
                    srcIndexInt = monoInput.Length / 2 - 2;
                    frac = 1;
                }
                
                short sample1 = (short)(monoInput[srcIndexInt * 2] | (monoInput[srcIndexInt * 2 + 1] << 8));
                short sample2 = (short)(monoInput[(srcIndexInt + 1) * 2] | (monoInput[(srcIndexInt + 1) * 2 + 1] << 8));
                short sample = (short)(sample1 * (1 - frac) + sample2 * frac);
                
                output[i * 2] = (byte)(sample & 0xFF);
                output[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            
            return output;
        }

        private byte[] ConvertFloatToInt16(byte[] input)
        {
            // 假设输入是 IEEE 浮点格式 (4 bytes per sample)
            if (input.Length % 4 != 0) return input;
            
            byte[] output = new byte[input.Length / 2];
            
            for (int i = 0, j = 0; i < input.Length; i += 4, j += 2)
            {
                // 读取浮点值
                float floatSample = BitConverter.ToSingle(input, i);
                
                // 转换为16位整数 (-1.0 to 1.0 -> -32768 to 32767)
                short intSample = (short)(floatSample * 32767f);
                
                output[j] = (byte)(intSample & 0xFF);
                output[j + 1] = (byte)((intSample >> 8) & 0xFF);
            }
            
            return output;
        }

        private byte[] ConvertStereoToMono(byte[] stereo)
        {
            // 输入是 16bit 立体声（4 bytes per frame）
            if (stereo.Length % 4 != 0) return stereo;
            
            byte[] mono = new byte[stereo.Length / 2];
            for (int i = 0, j = 0; i < stereo.Length; i += 4, j += 2)
            {
                short left = (short)(stereo[i] | (stereo[i + 1] << 8));
                short right = (short)(stereo[i + 2] | (stereo[i + 3] << 8));
                short monoSample = (short)((left + right) / 2);
                
                mono[j] = (byte)(monoSample & 0xFF);
                mono[j + 1] = (byte)((monoSample >> 8) & 0xFF);
            }
            return mono;
        }

        private async Task ProcessResultsLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_resultQueue.TryDequeue(out var segment))
                    {
                        // 翻译（如果是英文）
                        if (segment.IsEnglish)
                        {
                            try
                            {
                                segment.TranslatedText = await _translationService.TranslateAsync(
                                    segment.OriginalText, "en", "zh");
                            }
                            catch
                            {
                                segment.TranslatedText = "[翻译失败]";
                            }
                        }

                        // 触发事件
                        SegmentRecognized?.Invoke(this, segment);
                        StatusChanged?.Invoke(this, $"识别: {segment.OriginalText.Substring(0, Math.Min(30, segment.OriginalText.Length))}...");
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch { }
            }
        }

        private byte[] ConvertToWavFormat(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);
            
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write((int)(36 + pcmData.Length));
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });
            writer.Write((int)16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write((int)sampleRate);
            writer.Write((int)(sampleRate * channels * bitsPerSample / 8));
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write((short)bitsPerSample);
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });
            writer.Write((int)pcmData.Length);
            writer.Write(pcmData);
            
            return ms.ToArray();
        }

        private bool IsEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var letters = text.Trim().Take(20).Where(char.IsLetter);
            return letters.Any() && letters.All(c => c <= 127);
        }

        public void Dispose()
        {
            StopProcessing();
            _processingCts?.Dispose();
            _audioQueue?.Dispose();
        }
    }
}
