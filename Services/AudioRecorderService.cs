using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading;

namespace AudioTranscriber.Services
{
    public class AudioRecorderService : IDisposable
    {
        private IWaveIn? _waveIn;
        private MemoryStream? _memoryStream;
        private WaveFileWriter? _waveWriter;
        private bool _isRecording;
        private readonly object _lockObject = new();

        public event EventHandler<byte[]>? AudioDataAvailable;
        public event EventHandler<Exception>? RecordingError;

        public bool IsRecording => _isRecording;
        public bool IsCapturingSystemSound { get; private set; }
        public WaveFormat? CurrentWaveFormat => _waveIn?.WaveFormat;

        /// <summary>
        /// 开始录制麦克风
        /// </summary>
        public void StartRecordingMicrophone(int deviceNumber = 0, int sampleRate = 16000, int channels = 1)
        {
            if (_isRecording)
                return;

            try
            {
                // 检查设备是否有效
                if (deviceNumber < 0 || deviceNumber >= WaveIn.DeviceCount)
                {
                    throw new ArgumentException($"无效的麦克风设备编号: {deviceNumber}");
                }

                lock (_lockObject)
                {
                    _memoryStream = new MemoryStream();
                    
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = deviceNumber,
                        WaveFormat = new WaveFormat(sampleRate, channels),
                        BufferMilliseconds = 100
                    };

                    _waveWriter = new WaveFileWriter(_memoryStream, _waveIn.WaveFormat);

                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    _waveIn.StartRecording();
                    _isRecording = true;
                    IsCapturingSystemSound = false;
                }
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"启动麦克风录音失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 开始捕获系统声音（扬声器输出）
        /// </summary>
        public void StartRecordingSystemSound(string deviceId, int sampleRate = 16000, int channels = 2)
        {
            if (_isRecording)
                return;

            try
            {
                lock (_lockObject)
                {
                    _memoryStream = new MemoryStream();
                    
                    // 使用 WASAPI Loopback 捕获系统声音
                    var enumerator = new MMDeviceEnumerator();
                    
                    // 获取默认播放设备（通常系统声音从这里捕获）
                    MMDevice? device = null;
                    
                    try
                    {
                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            device = enumerator.GetDevice(deviceId);
                        }
                    }
                    catch
                    {
                        // 如果指定设备失败，使用默认设备
                    }
                    
                    // 如果指定设备无效，使用默认播放设备
                    device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    
                    if (device == null)
                    {
                        throw new InvalidOperationException("无法获取音频播放设备");
                    }

                    // 使用 WasapiLoopbackCapture 捕获系统声音
                    _waveIn = new WasapiLoopbackCapture(device);
                    
                    _waveWriter = new WaveFileWriter(_memoryStream, _waveIn.WaveFormat);

                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    _waveIn.StartRecording();
                    _isRecording = true;
                    IsCapturingSystemSound = true;
                }
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"启动系统声音捕获失败: {ex.Message}", ex);
            }
        }

        public void StopRecording()
        {
            if (!_isRecording)
                return;

            try
            {
                lock (_lockObject)
                {
                    _waveIn?.StopRecording();
                }
            }
            catch (Exception ex)
            {
                RecordingError?.Invoke(this, ex);
            }
        }

        public byte[]? GetRecordedAudio()
        {
            lock (_lockObject)
            {
                if (_memoryStream == null)
                    return null;

                try
                {
                    _waveWriter?.Flush();
                    return _memoryStream.ToArray();
                }
                catch
                {
                    return null;
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_waveWriter != null && e.BytesRecorded > 0)
                    {
                        _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        _waveWriter.Flush();
                    }
                }

                // 复制音频数据用于实时处理
                if (e.BytesRecorded > 0)
                {
                    var buffer = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
                    AudioDataAvailable?.Invoke(this, buffer);
                }
            }
            catch (Exception ex)
            {
                RecordingError?.Invoke(this, ex);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            
            if (e.Exception != null)
            {
                RecordingError?.Invoke(this, e.Exception);
            }

            Cleanup();
        }

        private void Cleanup()
        {
            lock (_lockObject)
            {
                try
                {
                    _waveWriter?.Dispose();
                }
                catch { }
                finally
                {
                    _waveWriter = null;
                }
                
                if (_waveIn != null)
                {
                    try
                    {
                        _waveIn.DataAvailable -= OnDataAvailable;
                        _waveIn.RecordingStopped -= OnRecordingStopped;
                        _waveIn.Dispose();
                    }
                    catch { }
                    finally
                    {
                        _waveIn = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            try
            {
                StopRecording();
                Cleanup();
                _memoryStream?.Dispose();
            }
            catch { }
        }
    }
}
