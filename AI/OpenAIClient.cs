using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace MiSound.AI.Local
{
    public sealed class OpenAIClient
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _systemPrompt;

        public OpenAIClient(string baseUrl, string apiKey, string model, string systemPrompt)
        {
            _baseUrl = (baseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
            _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? "你是一个简洁的中文语音助手。" : systemPrompt;
            _http.Timeout = TimeSpan.FromSeconds(90);
        }

        public async Task<string> AskAsync(string question)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.StartsWith("填写"))
            {
                throw new Exception("请先在 config.json 中填写 AI.ApiKey。");
            }
            var body = new JObject
            {
                ["model"] = _model,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = _systemPrompt },
                    new JObject { ["role"] = "user", ["content"] = question }
                },
                ["temperature"] = 0.7
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception("AI 接口返回 HTTP " + (int)response.StatusCode + "：" + text);
                    }
                    var json = JObject.Parse(text);
                    var answer = (string)json["choices"]?[0]?["message"]?["content"];
                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        throw new Exception("AI 返回内容为空：" + text);
                    }
                    return answer.Trim();
                }
            }
        }

        public async Task AskAsync(string question, Action<string> onSentence = null)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.StartsWith("填写"))
            {
                throw new Exception("请先在 config.json 中填写 AI.ApiKey。");
            }

            var body = new JObject
            {
                ["model"] = _model,
                ["stream"] = true,
                ["temperature"] = 0.7,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = _systemPrompt },
                    new JObject { ["role"] = "user", ["content"] = question }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                // 关键:ResponseHeadersRead,拿到响应头就开始读流,不等全部内容
                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new Exception("AI 接口返回 HTTP " + (int)response.StatusCode + ":" + err);
                    }

                    var full = new StringBuilder();
                    var buf = new StringBuilder();
                    var firstSent = false;

                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (!line.StartsWith("data:"))
                            {
                                continue;
                            }
                            var data = line.Substring(5).Trim();
                            if (data.Length == 0)
                            {
                                continue;
                            }
                            if (data == "[DONE]")
                            {
                                break;
                            }

                            string delta;
                            try
                            {
                                delta = (string)JObject.Parse(data)["choices"]?[0]?["delta"]?["content"];
                            }
                            catch
                            {
                                continue;
                            }
                            if (string.IsNullOrEmpty(delta))
                            {
                                continue;
                            }

                            full.Append(delta);
                            buf.Append(delta);


                            onSentence?.Invoke(delta);
                            //// 按句切分回调
                            //string seg;
                            //while ((seg = TakeSentence(buf, ref firstSent)) != null)
                            //{
                            //    onSentence?.Invoke(seg);
                            //}
                        }
                    }

                    // 收尾:剩余不足一句的内容也要播
                    //var rest = buf.ToString().Trim();
                    //if (rest.Length > 0)
                    //{
                    //    onSentence?.Invoke(rest);
                    //}

                    //var answer = full.ToString().Trim();
                    //if (answer.Length == 0)
                    //{
                    //    throw new Exception("AI 返回内容为空");
                    //}
                    //return answer;
                }
            }
        }

        // 从缓冲区取出一个完整句子;首句遇逗号也提前切,降低首字延迟
        private static string TakeSentence(StringBuilder buf, ref bool firstSent)
        {
            var s = buf.ToString();
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                bool end = "。!?!?;;\n".IndexOf(c) >= 0;
                bool soft = !firstSent && i >= 6 && ",,、:".IndexOf(c) >= 0;
                if (end || soft)
                {
                    var seg = s.Substring(0, i + 1).Trim();
                    buf.Remove(0, i + 1);
                    if (seg.Length == 0) continue;
                    firstSent = true;
                    return seg;
                }
            }
            return null;
        }
    }
}
