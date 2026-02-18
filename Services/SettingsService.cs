using System;
using System.IO;
using System.Text.Json;

namespace AudioTranscriber.Services
{
    public class AppSettings
    {
        public string SaveDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
            "AudioTranscriber");
        
        public string SelectedDeviceId { get; set; } = "0";
        public bool IsSystemSound { get; set; } = false;
        public bool EnableTranslation { get; set; } = true;
        public bool AutoSave { get; set; } = false;
        public string WindowMode { get; set; } = "Normal"; // Normal, SubtitleBar, Mini
    }

    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public SettingsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioTranscriber");
            
            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);
            
            _settingsPath = Path.Combine(appDataPath, "settings.json");
            _settings = LoadSettings();
        }

        public AppSettings Settings => _settings;

        private AppSettings LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        return settings;
                }
                catch { }
            }

            // 默认设置
            return new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        public void EnsureSaveDirectoryExists()
        {
            if (!Directory.Exists(_settings.SaveDirectory))
                Directory.CreateDirectory(_settings.SaveDirectory);
        }
    }
}
