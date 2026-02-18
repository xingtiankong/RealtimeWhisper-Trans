using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioTranscriber.Models
{
    public class TranscriptSegment : INotifyPropertyChanged
    {
        private string _originalText = string.Empty;
        private string _translatedText = string.Empty;
        private string _language = "auto";
        private long _startTimeSeconds;
        private long _endTimeSeconds;
        private bool _isEnglish;
        private bool _isFinal;

        public string OriginalText
        {
            get => _originalText;
            set { _originalText = value; OnPropertyChanged(); }
        }

        public string TranslatedText
        {
            get => _translatedText;
            set { _translatedText = value; OnPropertyChanged(); }
        }

        public string Language
        {
            get => _language;
            set { _language = value; OnPropertyChanged(); }
        }

        public long StartTimeSeconds
        {
            get => _startTimeSeconds;
            set { _startTimeSeconds = value; OnPropertyChanged(); }
        }

        public long EndTimeSeconds
        {
            get => _endTimeSeconds;
            set { _endTimeSeconds = value; OnPropertyChanged(); }
        }

        public TimeSpan StartTime => TimeSpan.FromSeconds(_startTimeSeconds);
        public TimeSpan EndTime => TimeSpan.FromSeconds(_endTimeSeconds);

        public bool IsEnglish
        {
            get => _isEnglish;
            set { _isEnglish = value; OnPropertyChanged(); }
        }

        public bool IsFinal
        {
            get => _isFinal;
            set { _isFinal = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum RecordingState
    {
        Idle,
        Recording,
        Processing,
        Error
    }

    public enum LanguageType
    {
        Auto,
        Chinese,
        English,
        Japanese
    }
}
