using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static AudioTranscriber.App;

namespace AudioTranscriber.Services
{
    /// <summary>
    /// Ollama 本地翻译服务 - 使用本地运行的 LLM 进行翻译
    /// 需要提前安装 Ollama 并下载模型
    /// 安装地址: https://ollama.com
    /// 推荐模型: qwen2.5:3b (轻量级，翻译效果好)
    /// </summary>
    public class OllamaTranslationService : ITranslationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _modelName;
        private bool _isAvailable;

        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// 创建 Ollama 翻译服务实例
        /// </summary>
        /// <param name="modelName">模型名称，默认 qwen2.5:3b</param>
        /// <param name="baseUrl">Ollama API 地址，默认 http://localhost:11434</param>
        public OllamaTranslationService(string modelName = "qwen2.5:3b", string baseUrl = "http://localhost:11434")
        {
            _modelName = modelName;
            _baseUrl = baseUrl;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            
            // 异步检查 Ollama 是否可用
            _ = CheckAvailabilityAsync();
        }

        private async Task CheckAvailabilityAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    
                    // 检查指定模型是否已安装
                    if (doc.RootElement.TryGetProperty("models", out var models))
                    {
                        foreach (var model in models.EnumerateArray())
                        {
                            if (model.TryGetProperty("name", out var name))
                            {
                                var modelName = name.GetString();
                                if (modelName?.StartsWith(_modelName) == true || 
                                    modelName == _modelName ||
                                    modelName?.Replace(":latest", "") == _modelName.Replace(":latest", ""))
                                {
                                    _isAvailable = true;
                                    App.LogInfo($"Ollama 翻译服务已就绪，模型: {_modelName}");
                                    return;
                                }
                            }
                        }
                    }
                    
                    App.LogInfo($"Ollama 已运行，但未找到模型 {_modelName}，请运行: ollama pull {_modelName}");
                    _isAvailable = false;
                }
                else
                {
                    App.LogInfo("Ollama 服务未运行，请启动 Ollama");
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                App.LogInfo($"Ollama 连接失败: {ex.Message}");
                _isAvailable = false;
            }
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!_isAvailable)
            {
                // 尝试重新检查一次
                await CheckAvailabilityAsync();
                if (!_isAvailable)
                {
                    return $"[Ollama 未就绪] {text}";
                }
            }

            try
            {
                // 构建翻译提示词
                var prompt = BuildTranslationPrompt(text, sourceLanguage, targetLanguage);
                
                var requestBody = new
                {
                    model = _modelName,
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.3,  // 低温度，更确定的翻译
                        num_predict = 200    // 限制输出长度
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                App.LogInfo($"Ollama 翻译请求: '{text.Substring(0, Math.Min(30, text.Length))}...'");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);
                
                if (doc.RootElement.TryGetProperty("response", out var responseElement))
                {
                    var translated = responseElement.GetString()?.Trim() ?? text;
                    // 清理可能的引号
                    translated = translated.Trim('"', '\'', '`');
                    App.LogInfo($"Ollama 翻译完成: '{text.Substring(0, Math.Min(20, text.Length))}...' -> '{translated.Substring(0, Math.Min(20, translated.Length))}...'");
                    return translated;
                }

                return text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.LogInfo($"Ollama 翻译失败: {ex.Message}");
                _isAvailable = false;  // 标记为不可用，下次会重试
                return $"[翻译失败] {text}";
            }
        }

        private string BuildTranslationPrompt(string text, string sourceLanguage, string targetLanguage)
        {
            var src = sourceLanguage?.ToLower() switch
            {
                "en" or "english" or "eng" => "English",
                "zh" or "chinese" => "Chinese",
                "ja" or "japanese" => "Japanese",
                "ko" or "korean" => "Korean",
                "auto" => "English",  // 默认英文
                _ => "English"
            };

            var tgt = targetLanguage?.ToLower() switch
            {
                "zh" or "chinese" or "zh-cn" => "Chinese",
                "en" or "english" => "English",
                "ja" or "japanese" => "Japanese",
                _ => "Chinese"
            };

            return $"Translate the following text from {src} to {tgt}. Only output the translation, no explanation:\n\n{text}";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}