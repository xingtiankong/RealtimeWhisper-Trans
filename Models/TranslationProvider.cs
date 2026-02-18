namespace AudioTranscriber.Models
{
    /// <summary>
    /// 翻译服务提供者信息
    /// </summary>
    public class TranslationProvider
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool RequiresInternet { get; set; }
        public bool RequiresLocalModel { get; set; }

        public TranslationProvider(string id, string name, string description = "", bool requiresInternet = false, bool requiresLocalModel = false)
        {
            Id = id;
            Name = name;
            Description = description;
            RequiresInternet = requiresInternet;
            RequiresLocalModel = requiresLocalModel;
        }
    }
}
