using Whisper.net;
using Whisper.net.Ggml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioTranscriber.Models;
using static AudioTranscriber.App;

namespace AudioTranscriber.Services
{
    public class WhisperRecognitionService : IDisposable
    {
        private WhisperFactory _whisperFactory;
        private WhisperProcessor _processor;
        private bool _isInitialized;
        private readonly string _modelPath;

        public event EventHandler<TranscriptSegment>? SegmentRecognized;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<float>? DownloadProgress;  // 下载进度 0-100

        public bool IsInitialized => _isInitialized;

        public WhisperRecognitionService(string? modelPath = null)
        {
            _modelPath = modelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "ggml-base.bin");
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
                return;

            try
            {
                // 确保模型文件存在
                await EnsureModelExistsAsync(cancellationToken);

                _whisperFactory = WhisperFactory.FromPath(_modelPath);
                
                // 创建处理器，支持多语言
                _processor = _whisperFactory.CreateBuilder()
                    .WithLanguage("auto")
                    .WithSegmentEventHandler(OnSegmentDetected)
                    .Build();
                
                App.LogInfo("Whisper模型加载成功");

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"初始化 Whisper 失败: {ex.Message}");
                throw;
            }
        }

        private async Task EnsureModelExistsAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(_modelPath))
                return;

            var directory = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // 下载模型（带进度显示）
            ErrorOccurred?.Invoke(this, "正在下载 Whisper 模型（约150MB），请稍候...");
            
            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(
                GgmlType.Base, 
                cancellationToken: cancellationToken);
            
            using var fileStream = File.Create(_modelPath);
            
            // 使用自定义缓冲复制并报告进度
            var totalBytes = modelStream.Length > 0 ? modelStream.Length : 150 * 1024 * 1024; // 预估150MB
            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            
            while ((read = await modelStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                totalRead += read;
                
                // 报告进度
                var progress = (float)(totalRead * 100 / totalBytes);
                DownloadProgress?.Invoke(this, Math.Min(progress, 99.9f));
            }
            
            DownloadProgress?.Invoke(this, 100f);
            ErrorOccurred?.Invoke(this, "模型下载完成！");
        }

        public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                App.LogInfo("Whisper: 模型未初始化，开始初始化...");
                await InitializeAsync(cancellationToken);
            }

            try
            {
                App.LogInfo($"Whisper: 开始处理音频数据，长度: {audioData.Length} bytes");
                using var memoryStream = new MemoryStream(audioData);
                var result = new List<string>();
                int segmentCount = 0;
                
                await foreach (var segment in _processor.ProcessAsync(memoryStream, cancellationToken))
                {
                    segmentCount++;
                    result.Add(segment.Text);
                    App.LogInfo($"Whisper: 识别到片段 {segmentCount}: {segment.Text}");
                }

                var finalResult = string.Join(" ", result).Trim();
                App.LogInfo($"Whisper: 最终识别结果: '{finalResult}', 片段数: {segmentCount}");
                return finalResult;
            }
            catch (OperationCanceledException)
            {
                App.LogInfo("Whisper: 识别超时/被取消");
                return string.Empty; // 不抛出异常，直接返回空
            }
            catch (Exception ex)
            {
                App.LogInfo($"Whisper: 识别异常: {ex.GetType().Name}: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"语音识别失败: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task ProcessStreamAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                await InitializeAsync(cancellationToken);

            try
            {
                using var memoryStream = new MemoryStream(audioData);
                await foreach (var segment in _processor.ProcessAsync(memoryStream, cancellationToken))
                {
                    var transcriptSegment = CreateTranscriptSegment(segment, true);
                    SegmentRecognized?.Invoke(this, transcriptSegment);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"处理音频流失败: {ex.Message}");
            }
        }

        private void OnSegmentDetected(SegmentData segment)
        {
            var transcriptSegment = CreateTranscriptSegment(segment, false);
            SegmentRecognized?.Invoke(this, transcriptSegment);
        }

        private TranscriptSegment CreateTranscriptSegment(SegmentData segment, bool isFinal)
        {
            var ts = new TranscriptSegment();
            ts.OriginalText = segment.Text.Trim();
            ts.Language = segment.Language ?? "auto";
            ts.IsEnglish = segment.Language == "en";
            ts.StartTimeSeconds = (long)segment.Start.TotalSeconds;
            ts.EndTimeSeconds = (long)segment.End.TotalSeconds;
            ts.IsFinal = isFinal;
            return ts;
        }

        public void Dispose()
        {
            _processor?.Dispose();
            _whisperFactory?.Dispose();
            _isInitialized = false;
        }
    }
}
