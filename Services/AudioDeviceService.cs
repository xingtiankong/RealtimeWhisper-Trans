using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioTranscriber.Services
{
    public class AudioDeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsInput { get; set; }
        public bool IsLoopback { get; set; }
    }

    public class AudioDeviceService
    {
        /// <summary>
        /// 获取所有音频输入设备（麦克风）
        /// </summary>
        public List<AudioDeviceInfo> GetInputDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo
                {
                    Id = i.ToString(),
                    Name = capabilities.ProductName,
                    IsInput = true,
                    IsLoopback = false
                });
            }
            return devices;
        }

        /// <summary>
        /// 获取所有音频输出设备（用于系统声音捕获）
        /// </summary>
        public List<AudioDeviceInfo> GetOutputDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            var enumerator = new MMDeviceEnumerator();
            
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = $"🖥️ 系统声音: {device.FriendlyName}",
                    IsInput = false,
                    IsLoopback = true
                });
            }
            
            return devices;
        }

        /// <summary>
        /// 获取所有可用设备（麦克风 + 系统声音）
        /// </summary>
        public List<AudioDeviceInfo> GetAllDevices()
        {
            var allDevices = new List<AudioDeviceInfo>();
            allDevices.AddRange(GetInputDevices());
            allDevices.AddRange(GetOutputDevices());
            return allDevices;
        }
    }
}
