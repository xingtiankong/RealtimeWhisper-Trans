using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AudioTranscriber.App;

namespace AudioTranscriber.Services
{
    /// <summary>
    /// 本地智能翻译服务 - 内置大型词库，完全离线运行
    /// </summary>
    public class LocalSmartTranslationService : ITranslationService
    {
        public bool IsAvailable => true;
        
        private readonly Dictionary<string, string> _dictionary;
        private readonly HashSet<string> _stopWords;
        
        public LocalSmartTranslationService()
        {
            _dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the", "a", "an" };
            
            InitializeDictionary();
            App.LogInfo($"本地智能翻译服务初始化完成，词库大小: {_dictionary.Count}");
        }
        
        private void InitializeDictionary()
        {
            // 代词
            AddRange(new[] {
                ("i", "我"), ("you", "你"), ("he", "他"), ("she", "她"), ("it", "它"),
                ("we", "我们"), ("they", "他们"), ("me", "我"), ("him", "他"), ("her", "她"),
                ("us", "我们"), ("them", "他们"), ("my", "我的"), ("your", "你的"), ("his", "他的"),
                ("this", "这个"), ("that", "那个"), ("these", "这些"), ("those", "那些"),
            });
            
            // Be动词和常用动词
            AddRange(new[] {
                ("be", "是"), ("is", "是"), ("am", "是"), ("are", "是"), ("was", "是"), ("were", "是"),
                ("do", "做"), ("does", "做"), ("did", "做了"), ("done", "完成"),
                ("have", "有"), ("has", "有"), ("had", "有过"),
                ("go", "去"), ("went", "去了"), ("come", "来"), ("came", "来了"),
                ("get", "得到"), ("got", "得到了"), ("make", "制作"), ("made", "制作了"),
                ("take", "拿"), ("took", "拿了"), ("see", "看"), ("saw", "看了"),
                ("look", "看"), ("know", "知道"), ("knew", "知道了"),
                ("think", "想"), ("thought", "想过"), ("say", "说"), ("said", "说了"),
                ("speak", "说话"), ("tell", "告诉"), ("told", "告诉了"),
                ("talk", "谈话"), ("ask", "问"), ("answer", "回答"),
                ("give", "给"), ("gave", "给了"), ("want", "想要"), ("need", "需要"),
                ("like", "喜欢"), ("love", "爱"), ("hate", "讨厌"),
                ("hope", "希望"), ("wish", "希望"), ("help", "帮助"),
                ("try", "尝试"), ("start", "开始"), ("begin", "开始"),
                ("end", "结束"), ("finish", "完成"), ("stop", "停止"),
                ("keep", "保持"), ("put", "放"), ("turn", "转动"),
                ("open", "打开"), ("close", "关闭"), ("show", "显示"),
                ("play", "播放"), ("run", "跑"), ("walk", "走"),
                ("eat", "吃"), ("ate", "吃了"), ("drink", "喝"),
                ("sleep", "睡觉"), ("work", "工作"), ("study", "学习"),
                ("buy", "买"), ("sell", "卖"), ("pay", "支付"),
                ("find", "找到"), ("lose", "失去"), ("call", "呼叫"),
                ("send", "发送"), ("feel", "感觉"), ("become", "成为"),
                ("leave", "离开"), ("arrive", "到达"), ("stay", "停留"),
                ("live", "生活"), ("die", "死亡"), ("kill", "杀死"),
            });
            
            // 常用名词
            AddRange(new[] {
                ("time", "时间"), ("day", "天"), ("year", "年"),
                ("man", "男人"), ("woman", "女人"), ("boy", "男孩"), ("girl", "女孩"),
                ("people", "人们"), ("person", "人"),
                ("family", "家庭"), ("father", "父亲"), ("mother", "母亲"),
                ("parent", "父母"), ("brother", "兄弟"), ("sister", "姐妹"),
                ("son", "儿子"), ("daughter", "女儿"), ("friend", "朋友"),
                ("world", "世界"), ("country", "国家"), ("city", "城市"),
                ("home", "家"), ("house", "房子"), ("room", "房间"),
                ("work", "工作"), ("job", "职业"),
                ("school", "学校"), ("teacher", "老师"), ("student", "学生"),
                ("book", "书"), ("word", "单词"), ("name", "名字"),
                ("water", "水"), ("food", "食物"), ("money", "钱"),
                ("hand", "手"), ("head", "头"), ("eye", "眼睛"),
                ("life", "生活"), ("death", "死亡"),
                ("today", "今天"), ("tomorrow", "明天"), ("yesterday", "昨天"),
                ("morning", "早上"), ("afternoon", "下午"), ("evening", "晚上"), ("night", "夜晚"),
            });
            
            // 形容词
            AddRange(new[] {
                ("good", "好"), ("bad", "坏"), ("new", "新"), ("old", "旧"),
                ("big", "大"), ("small", "小"), ("high", "高"), ("low", "低"),
                ("long", "长"), ("short", "短"), ("fast", "快"), ("slow", "慢"),
                ("hot", "热"), ("cold", "冷"), ("happy", "快乐"), ("sad", "悲伤"),
                ("beautiful", "美丽"), ("ugly", "丑陋"),
                ("easy", "容易"), ("difficult", "困难"),
                ("important", "重要"), ("necessary", "必要"),
                ("possible", "可能"), ("impossible", "不可能"),
            });
            
            // 常用短语
            AddRange(new[] {
                ("hello", "你好"), ("hi", "嗨"),
                ("good morning", "早上好"), ("good afternoon", "下午好"),
                ("good evening", "晚上好"), ("good night", "晚安"),
                ("goodbye", "再见"), ("bye", "拜拜"),
                ("see you", "再见"), ("see you later", "回头见"),
                ("nice to meet you", "很高兴认识你"),
                ("how are you", "你好吗"),
                ("thank you", "谢谢你"), ("thanks", "谢谢"),
                ("thank you very much", "非常感谢"),
                ("you're welcome", "不客气"),
                ("sorry", "对不起"), ("excuse me", "打扰一下"),
                ("yes", "是的"), ("no", "不"),
                ("i love you", "我爱你"),
                ("i'm sorry", "对不起"),
                ("i don't know", "我不知道"),
                ("i don't understand", "我不明白"),
                ("i agree", "我同意"),
                ("i think so", "我想是的"),
                ("congratulations", "恭喜"),
                ("happy birthday", "生日快乐"),
                ("good luck", "祝你好运"),
                ("take care", "保重"),
            });
        }
        
        private void AddRange(IEnumerable<(string en, string zh)> words)
        {
            foreach (var (en, zh) in words)
            {
                if (!_dictionary.ContainsKey(en))
                {
                    _dictionary[en] = zh;
                }
            }
        }
        
        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult(string.Empty);
                
            var input = text.ToLower().Trim();
            
            // 1. 完整短语匹配
            if (_dictionary.TryGetValue(input, out var directTranslation))
            {
                return Task.FromResult(directTranslation);
            }
            
            // 2. 查找包含的最长短语
            var bestMatch = "";
            var bestTranslation = "";
            foreach (var kvp in _dictionary)
            {
                if (input.Contains(kvp.Key) && kvp.Key.Length > bestMatch.Length)
                {
                    bestMatch = kvp.Key;
                    bestTranslation = kvp.Value;
                }
            }
            
            if (!string.IsNullOrEmpty(bestTranslation))
            {
                return Task.FromResult(bestTranslation);
            }
            
            // 3. 逐词翻译
            var words = input.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var translated = new List<string>();
            
            foreach (var word in words)
            {
                if (_stopWords.Contains(word))
                    continue;
                    
                if (_dictionary.TryGetValue(word, out var wTranslation))
                {
                    translated.Add(wTranslation);
                }
            }
            
            if (translated.Count > 0)
            {
                return Task.FromResult(string.Join("", translated));
            }
            
            // 4. 无法翻译
            return Task.FromResult($"☆ {text}");
        }
    }
}