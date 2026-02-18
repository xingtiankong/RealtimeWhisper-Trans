using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using static AudioTranscriber.App;

namespace AudioTranscriber.Services
{
    public interface ITranslationService
    {
        Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
        bool IsAvailable { get; }
    }

    /// <summary>
    /// 本地 ONNX 翻译服务 - 使用 Opus-MT 模型
    /// 模型下载地址: https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/tree/main/onnx
    /// </summary>
    public class LocalOnnxTranslationService : ITranslationService, IDisposable
    {
        private InferenceSession? _session;
        private readonly string _modelPath;
        private readonly Dictionary<string, int> _sourceVocab;
        private readonly Dictionary<int, string> _targetVocab;
        private readonly string _vocabDir;
        private bool _isInitialized;

        public bool IsAvailable => _isInitialized;

        public LocalOnnxTranslationService(string? modelDir = null)
        {
            _vocabDir = modelDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "opus-mt-en-zh");
            _modelPath = Path.Combine(_vocabDir, "model.onnx");
            _sourceVocab = new Dictionary<string, int>();
            _targetVocab = new Dictionary<int, string>();
            
            InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // 检查并下载模型
                if (!File.Exists(_modelPath))
                {
                    App.LogInfo("本地翻译模型不存在，开始下载...");
                    await DownloadModelAsync(_vocabDir);
                }

                // 加载词表
                await LoadVocabularyAsync();
                
                // 创建 ONNX Session
                var options = new SessionOptions
                {
                    InterOpNumThreads = 2,
                    IntraOpNumThreads = 2,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                
                _session = new InferenceSession(_modelPath, options);
                _isInitialized = true;
                App.LogInfo("本地翻译模型加载成功");
            }
            catch (Exception ex)
            {
                App.LogInfo($"本地翻译模型加载失败: {ex.Message}");
                _isInitialized = false;
            }
        }

        private async Task LoadVocabularyAsync()
        {
            // 加载源语言词表 (English)
            var sourceVocabPath = Path.Combine(_vocabDir, "source.spm");
            var targetVocabPath = Path.Combine(_vocabDir, "target.spm");
            
            // 如果存在 vocab.json，使用它
            var vocabJsonPath = Path.Combine(_vocabDir, "vocab.json");
            if (File.Exists(vocabJsonPath))
            {
                var json = await File.ReadAllTextAsync(vocabJsonPath);
                var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (vocab != null)
                {
                    foreach (var kvp in vocab)
                    {
                        _sourceVocab[kvp.Key] = kvp.Value;
                        _targetVocab[kvp.Value] = kvp.Key;
                    }
                }
            }
            
            // 如果没有词表，使用简单的字符级处理
            if (_sourceVocab.Count == 0)
            {
                App.LogInfo("未找到词表文件，使用简单分词");
                // 添加一些基础词汇
                var basicTokens = new[] { "<pad>", "<unk>", "<s>", "</s>", "en", "zh" };
                for (int i = 0; i < basicTokens.Length; i++)
                {
                    _sourceVocab[basicTokens[i]] = i;
                    _targetVocab[i] = basicTokens[i];
                }
            }
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!_isInitialized || _session == null)
            {
                App.LogInfo("本地翻译模型未加载，使用备用翻译");
                return $"[本地翻译不可用] {text}";
            }

            try
            {
                // 简单的分词（实际应该使用 SentencePiece）
                var tokens = SimpleTokenize(text);
                
                // 转换为输入张量
                var inputIds = tokens.Select(t => _sourceVocab.GetValueOrDefault(t, _sourceVocab.GetValueOrDefault("<unk>", 1))).ToArray();
                var inputTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
                for (int i = 0; i < inputIds.Length; i++)
                {
                    inputTensor[0, i] = inputIds[i];
                }

                // 创建输入
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
                };

                // 运行推理
                using var results = _session.Run(inputs);
                
                // 解析输出
                var outputTensor = results.First().AsTensor<long>();
                var translatedTokens = new List<string>();
                
                for (int i = 0; i < outputTensor.Length; i++)
                {
                    var tokenId = (int)outputTensor.GetValue(i);
                    if (tokenId == _sourceVocab.GetValueOrDefault("</s>", 2)) // 结束符
                        break;
                    if (_targetVocab.TryGetValue(tokenId, out var token))
                    {
                        translatedTokens.Add(token);
                    }
                }

                var result = string.Join("", translatedTokens).Replace("▁", " ").Trim();
                App.LogInfo($"本地翻译: '{text}' -> '{result}'");
                return result;
            }
            catch (Exception ex)
            {
                App.LogInfo($"本地翻译失败: {ex.Message}");
                return $"[翻译失败] {text}";
            }
        }

        private List<string> SimpleTokenize(string text)
        {
            // 简单的分词：按空格分割并添加前缀
            var tokens = new List<string> { "<s>" };
            var words = text.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                tokens.Add($"▁{word}"); // SentencePiece 使用 ▁ 表示词首
            }
            
            tokens.Add("</s>");
            return tokens;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }

        /// <summary>
        /// 下载 Opus-MT 模型
        /// </summary>
        public static async Task DownloadModelAsync(string modelDir, IProgress<float>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(modelDir);
            
            // 使用 Hugging Face 的镜像下载模型
            var baseUrl = "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main";
            
            var files = new[]
            {
                "model.onnx",
                "vocab.json",
                "source.spm",
                "target.spm",
                "config.json"
            };

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            
            foreach (var file in files)
            {
                var url = $"{baseUrl}/{file}";
                var path = Path.Combine(modelDir, file);
                
                if (File.Exists(path))
                {
                    progress?.Report(1.0f);
                    continue;
                }

                try
                {
                    var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;
                    
                    await using var fs = File.Create(path);
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloadedBytes += read;
                        
                        if (totalBytes > 0)
                        {
                            progress?.Report((float)downloadedBytes / totalBytes);
                        }
                    }
                    
                    App.LogInfo($"下载完成: {file}");
                }
                catch (Exception ex)
                {
                    App.LogInfo($"下载失败 {file}: {ex.Message}");
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 使用 Google Translate 免费 API 的翻译服务
    /// </summary>
    public class GoogleTranslationService : ITranslationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private static readonly Random _random = new();

        public bool IsAvailable => true;

        public GoogleTranslationService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // 将语言代码转换为 Google 格式
            sourceLanguage = ConvertLanguageCode(sourceLanguage);
            targetLanguage = ConvertLanguageCode(targetLanguage);

            try
            {
                // 构建 Google Translate API URL
                var encodedText = Uri.EscapeDataString(text);
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t&q={encodedText}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    App.LogInfo($"Google翻译HTTP错误: {response.StatusCode}");
                    return $"[翻译失败] {text}";
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // 解析 Google Translate 响应
                var translated = ParseGoogleTranslateResponse(json);
                
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    App.LogInfo($"翻译成功: '{text}' -> '{translated}'");
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
                App.LogInfo($"Google翻译失败: {ex.Message}");
                return $"[翻译失败] {text}";
            }
        }

        private string ParseGoogleTranslateResponse(string json)
        {
            try
            {
                // Google返回的是一个嵌套数组，我们需要提取翻译结果
                // 格式: [[["翻译文本", "原文", ...]], ...]
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    return string.Empty;

                // 第一个元素包含翻译结果
                var firstArray = root[0];
                if (firstArray.ValueKind != JsonValueKind.Array)
                    return string.Empty;

                var results = new List<string>();
                foreach (var item in firstArray.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 1)
                    {
                        var translatedText = item[0].GetString();
                        if (!string.IsNullOrEmpty(translatedText))
                        {
                            results.Add(translatedText);
                        }
                    }
                }

                return string.Join("", results);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ConvertLanguageCode(string code)
        {
            return code?.ToLower() switch
            {
                "en" or "english" or "eng" => "en",
                "zh" or "chinese" or "zh-cn" or "zh-tw" => "zh-CN",
                "ja" or "japanese" => "ja",
                "ko" or "korean" => "ko",
                "fr" or "french" => "fr",
                "de" or "german" => "de",
                "es" or "spanish" => "es",
                "ru" or "russian" => "ru",
                "auto" => "auto",
                _ => code ?? "auto"
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// 本地 ONNX 翻译服务 - 使用 Argos Translate 或类似模型
    /// </summary>
    public class LocalTranslationService : ITranslationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;

        public bool IsAvailable => !string.IsNullOrEmpty(_apiUrl);

        public LocalTranslationService(string apiUrl = "http://localhost:5000/translate")
        {
            _apiUrl = apiUrl;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            try
            {
                var requestBody = new
                {
                    q = text,
                    source = sourceLanguage,
                    target = targetLanguage,
                    format = "text"
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_apiUrl, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);
                
                if (doc.RootElement.TryGetProperty("translatedText", out var translatedElement))
                {
                    return translatedElement.GetString() ?? text;
                }

                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"翻译失败: {ex.Message}");
                return $"[翻译不可用] {text}";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// 增强的字典翻译服务 - 包含更多短语和简单规则
    /// </summary>
    public class EnhancedDictionaryTranslationService : ITranslationService
    {
        public bool IsAvailable => true;
        
        private readonly Dictionary<string, string> _translations;
        private readonly List<string> _skipWords = new() { "the", "a", "an", "is", "are", "was", "were" };
        
        public EnhancedDictionaryTranslationService()
        {
            _translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 问候
                ["hello"] = "你好",
                ["hi"] = "嗨",
                ["hey"] = "嘿",
                ["good morning"] = "早上好",
                ["good afternoon"] = "下午好",
                ["good evening"] = "晚上好",
                ["good night"] = "晚安",
                ["goodbye"] = "再见",
                ["bye"] = "再见",
                ["see you"] = "再见",
                ["see you later"] = "回头见",
                
                // 感谢
                ["thank you"] = "谢谢你",
                ["thanks"] = "谢谢",
                ["thank you very much"] = "非常感谢",
                ["thanks a lot"] = "多谢",
                ["you're welcome"] = "不客气",
                ["no problem"] = "没问题",
                
                // 礼貌用语
                ["please"] = "请",
                ["sorry"] = "对不起",
                ["excuse me"] = "打扰一下",
                ["pardon me"] = "请原谅",
                
                // 肯定/否定
                ["yes"] = "是的",
                ["no"] = "不",
                ["okay"] = "好的",
                ["ok"] = "好的",
                ["sure"] = "当然",
                ["of course"] = "当然",
                ["no way"] = "不可能",
                
                // 询问
                ["how are you"] = "你好吗",
                ["what's up"] = "怎么了",
                ["how's it going"] = "最近怎么样",
                ["nice to meet you"] = "很高兴认识你",
                ["what's your name"] = "你叫什么名字",
                ["my name is"] = "我的名字是",
                ["i am"] = "我是",
                ["where are you from"] = "你来自哪里",
                ["how old are you"] = "你多大了",
                
                // 情感
                ["i love you"] = "我爱你",
                ["i like you"] = "我喜欢你",
                ["i miss you"] = "我想你",
                ["i hate you"] = "我讨厌你",
                
                // 祝贺
                ["congratulations"] = "恭喜",
                ["happy birthday"] = "生日快乐",
                ["good luck"] = "祝你好运",
                ["take care"] = "保重",
                ["get well soon"] = "早日康复",
                ["have a good day"] = "祝你今天愉快",
                ["have a nice trip"] = "旅途愉快",
                
                // 日常
                ["what time is it"] = "现在几点了",
                ["where is the bathroom"] = "洗手间在哪里",
                ["how much is this"] = "这个多少钱",
                ["i don't understand"] = "我不明白",
                ["i don't know"] = "我不知道",
                ["do you speak chinese"] = "你会说中文吗",
                ["i speak a little english"] = "我会说一点英语",
                
                // 常用词
                ["help"] = "帮助",
                ["stop"] = "停止",
                ["wait"] = "等等",
                ["come on"] = "来吧",
                ["let's go"] = "走吧",
                ["hurry up"] = "快点",
                ["be careful"] = "小心",
                ["never mind"] = "没关系",
                ["it doesn't matter"] = "没关系",
                
                // 餐厅
                ["i'm hungry"] = "我饿了",
                ["i'm thirsty"] = "我渴了",
                ["i'm tired"] = "我累了",
                ["delicious"] = "美味",
                ["check please"] = "买单",
                
                // 购物
                ["too expensive"] = "太贵了",
                ["can you give me a discount"] = "能给我打折吗",
                ["i'll take it"] = "我买了",
                ["just looking"] = "随便看看",
            };
        }

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult(string.Empty);
                
            if (sourceLanguage != "en" && sourceLanguage != "auto")
                return Task.FromResult(text);

            var input = text.ToLower().Trim();
            
            // 1. 尝试完整匹配
            if (_translations.TryGetValue(input, out var directTranslation))
            {
                return Task.FromResult(directTranslation);
            }
            
            // 2. 尝试短语匹配（找最长的匹配）
            var bestMatch = "";
            var bestMatchLen = 0;
            
            foreach (var kvp in _translations)
            {
                if (input.Contains(kvp.Key) && kvp.Key.Length > bestMatchLen)
                {
                    bestMatch = kvp.Value;
                    bestMatchLen = kvp.Key.Length;
                }
            }
            
            if (!string.IsNullOrEmpty(bestMatch))
            {
                return Task.FromResult(bestMatch);
            }
            
            // 3. 简单分词翻译
            var words = input.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var translatedWords = new List<string>();
            
            foreach (var word in words)
            {
                var cleanWord = word.Trim();
                if (_skipWords.Contains(cleanWord))
                    continue;
                    
                if (_translations.TryGetValue(cleanWord, out var wordTranslation))
                {
                    translatedWords.Add(wordTranslation);
                }
            }
            
            if (translatedWords.Count > 0)
            {
                return Task.FromResult(string.Join(" ", translatedWords));
            }
            
            // 4. 无法翻译，返回原文加标记
            return Task.FromResult($"☆ {text}");
        }
    }

    /// <summary>
    /// 百度翻译 API 服务 - 国内访问快，每月5万字符免费
    /// 申请地址: https://fanyi-api.baidu.com/
    /// </summary>
    public class BaiduTranslationService : ITranslationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _appId;
        private readonly string _secretKey;
        private readonly bool _hasCredentials;

        public bool IsAvailable => _hasCredentials;

        public BaiduTranslationService(string? appId = null, string? secretKey = null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            
            // 优先使用传入的参数，否则使用默认值（用户需要替换）
            _appId = appId ?? "YOUR_APP_ID";
            _secretKey = secretKey ?? "YOUR_SECRET_KEY";
            
            // 检查是否有有效密钥
            _hasCredentials = _appId != "YOUR_APP_ID" && _secretKey != "YOUR_SECRET_KEY" 
                           && !string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_secretKey);
            
            if (!_hasCredentials)
            {
                App.LogInfo("百度翻译: 未配置API密钥，请在代码中设置 YOUR_APP_ID 和 YOUR_SECRET_KEY");
                App.LogInfo("申请地址: https://fanyi-api.baidu.com/");
            }
        }

        public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!_hasCredentials)
            {
                return $"[百度翻译未配置] {text}";
            }

            try
            {
                // 构建请求参数
                var salt = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var sign = GetMd5Hash(_appId + text + salt + _secretKey);
                
                var from = ConvertToBaiduLangCode(sourceLanguage);
                var to = ConvertToBaiduLangCode(targetLanguage);
                
                var url = "https://fanyi-api.baidu.com/api/trans/vip/translate";
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["q"] = text,
                    ["from"] = from,
                    ["to"] = to,
                    ["appid"] = _appId,
                    ["salt"] = salt,
                    ["sign"] = sign
                });

                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                using var doc = JsonDocument.Parse(json);
                
                // 检查错误
                if (doc.RootElement.TryGetProperty("error_code", out var errorCode))
                {
                    var errorMsg = doc.RootElement.TryGetProperty("error_msg", out var msg) 
                        ? msg.GetString() : "未知错误";
                    App.LogInfo($"百度翻译错误: {errorCode} - {errorMsg}");
                    return $"[翻译错误] {text}";
                }
                
                // 解析翻译结果
                if (doc.RootElement.TryGetProperty("trans_result", out var resultArray))
                {
                    var translations = new List<string>();
                    foreach (var item in resultArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("dst", out var dst))
                        {
                            translations.Add(dst.GetString() ?? "");
                        }
                    }
                    
                    var translated = string.Join("", translations);
                    App.LogInfo($"百度翻译: '{text}' -> '{translated}'");
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
                App.LogInfo($"百度翻译失败: {ex.Message}");
                return $"[翻译失败] {text}";
            }
        }

        private string GetMd5Hash(string input)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private string ConvertToBaiduLangCode(string code)
        {
            return code?.ToLower() switch
            {
                "en" or "english" or "eng" => "en",
                "zh" or "chinese" or "zh-cn" => "zh",
                "zh-tw" or "zh-hk" => "cht",
                "ja" or "japanese" => "jp",
                "ko" or "korean" => "kor",
                "fr" or "french" => "fra",
                "de" or "german" => "de",
                "es" or "spanish" => "spa",
                "ru" or "russian" => "ru",
                "auto" => "auto",
                _ => "auto"
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
