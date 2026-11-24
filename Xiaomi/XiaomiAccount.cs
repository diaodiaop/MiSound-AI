using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MiSound.AI.Local.Xiaomi
{
    /// <summary>小米非公开云接口认证。passToken 用于换取不同服务 SID 的 serviceToken。</summary>
    public sealed class XiaomiAccount : IDisposable
    {
        private const string AccountBase = "https://account.xiaomi.com";
        private const string LoginUa = "APP/com.xiaomi.mihome APPV/11.3.203 iosPassportSDK/4.2.50 iOS/26.3.1 MK/aVBob25lMTcsMg== DEVT/aVBob25l DEVS/aU9T BRA/QXBwbGU= L/zh_CN";
        private readonly CookieContainer _cookies = new CookieContainer();
        private readonly HttpClient _http;
        private readonly string _userId;
        private readonly string _passToken;
        private readonly string _deviceId;
        private readonly Dictionary<string, string> _serviceTokens = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _ssecurity = new Dictionary<string, string>();

        public XiaomiAccount(string userId, string passToken, string deviceId)
        {
            _userId = userId;
            _passToken = passToken;
            _deviceId = deviceId;
            var handler = new HttpClientHandler { CookieContainer = _cookies, AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(30);
            AddCookie(AccountBase, "sdkVersion", "3.9");
            AddCookie(AccountBase, "deviceId", _deviceId);
            AddCookie(AccountBase, "userId", _userId);
            AddCookie(AccountBase, "passToken", _passToken);
        }

        public async Task EnsureServiceTokenAsync(string sid)
        {
            string token;
            if (_serviceTokens.TryGetValue(sid, out token) && !string.IsNullOrEmpty(token)) return;

            var url = AccountBase + "/pass/serviceLogin?sid=" + Uri.EscapeDataString(sid) + "&_json=true";
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", LoginUa);
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var json = ParseXiaomiJson(body);
                    if (json.Value<int?>("code") != 0)
                    {
                        throw new Exception("小米认证失败（SID=" + sid + "）：" + json.ToString());
                    }

                    var userId = json.Value<string>("userId");
                    var location = json.Value<string>("location");
                    var nonce = json.Value<string>("nonce");
                    var security = json.Value<string>("ssecurity");
                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(location) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(security))
                    {
                        throw new Exception("小米认证响应缺少必要字段。请确认 passToken 有效，或账号是否触发了额外验证。响应：" + json.ToString());
                    }

                    var clientSign = Convert.ToBase64String(Sha1(Encoding.UTF8.GetBytes("nonce=" + nonce + "&" + security)));
                    var separator = location.Contains("?") ? "&" : "?";
                    using (var tokenResponse = await _http.GetAsync(location + separator + "clientSign=" + Uri.EscapeDataString(clientSign)).ConfigureAwait(false))
                    {
                        tokenResponse.EnsureSuccessStatusCode();
                        await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }

                    // 根据 SID 区分服务域名，不能混用不同服务的 Token
                    string serviceHost = sid == "micoapi" ? "https://api2.mina.mi.com" : "https://api.io.mi.com";

                    var serviceToken = FindCookie(serviceHost, "serviceToken") ?? FindCookie(location, "serviceToken");


                    if (string.IsNullOrEmpty(serviceToken))
                    {
                        throw new Exception("认证流程已完成，但没有从 Cookie 中取得 serviceToken。小米可能调整了登录流程。");
                    }

                    _serviceTokens[sid] = serviceToken;
                    _ssecurity[sid] = security;


                    AddCookie(serviceHost, "userId", userId);
                    AddCookie(serviceHost, "serviceToken", serviceToken);

                    if (sid == "xiaomiio")
                    {
                        AddCookie(serviceHost, "PassportDeviceId", _deviceId);
                    }


                    Console.WriteLine("[认证] SID=" + sid + " serviceToken 获取成功（不会显示凭证内容）。");
                }
            }
        }

        public string GetSecurity(string sid) { return _ssecurity[sid]; }
        public string UserId { get { return _userId; } }
        public HttpClient Http { get { return _http; } }

        public async Task<JObject> SendMiIoTAsync(string path, JObject data)
        {
            await EnsureServiceTokenAsync("xiaomiio").ConfigureAwait(false);
            var ssecurity = GetSecurity("xiaomiio");
            var dataText = data.ToString(Newtonsoft.Json.Formatting.None);
            var nonce = CreateNonce();
            var signedNonce = SignNonce(ssecurity, nonce);
            var message = path + "&" + signedNonce + "&" + nonce + "&data=" + dataText;
            byte[] key = Convert.FromBase64String(signedNonce);
            byte[] signature;
            using (var hmac = new HMACSHA256(key)) signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            var form = new Dictionary<string, string>
            {
                { "_nonce", nonce }, { "data", dataText }, { "signature", Convert.ToBase64String(signature) }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.io.mi.com/app" + path))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "iOS-14.4-6.0.103-iPhone12,3--D7744744F7AF32F0544445285880DD63E47D9BE9-" + _userId + "-iPhone");
                request.Headers.TryAddWithoutValidation("x-xiaomi-protocal-flag-cli", "PROTOCAL-HTTP2");
                request.Content = new FormUrlEncodedContent(form);
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var json = JObject.Parse(body);
                    if (json.Value<int?>("code") != 0)
                    {
                        throw new Exception("MIoT 请求失败：" + json.ToString());
                    }
                    return json;
                }
            }
        }


        public async Task<JObject> GetConversationAsync(string deviceId, string hardware, long timestamp, int limit)
        {
            await EnsureServiceTokenAsync("micoapi").ConfigureAwait(false);

            string serviceToken = _serviceTokens["micoapi"];

            string url = "https://userprofile.mina.mi.com/device_profile/v2/conversation" + "?source=dialogu" + "&hardware=" + Uri.EscapeDataString(hardware) + "&timestamp=" + timestamp + "&limit=" + limit;

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 10; 000; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/119.0.6045.193 Mobile Safari/537.36 /XiaoMi/HybridView/ micoSoundboxApp/i appVersion/A_2.4.40");

                request.Headers.TryAddWithoutValidation("Referer", "https://userprofile.mina.mi.com/dialogue-note/index.html");

                var host = "https://userprofile.mina.mi.com";

                AddCookie(host, "deviceId", deviceId);
                AddCookie(host, "serviceToken", serviceToken);
                AddCookie(host, "userId", _userId);

                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    //Console.WriteLine("[对话接口] HTTP: " + (int)response.StatusCode);
                    //Console.WriteLine("[对话接口] URL: " + url);
                    //Console.WriteLine("[对话接口] BODY: " + body);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception("对话接口 HTTP " + (int)response.StatusCode + "：" + body);
                    }

                    return JObject.Parse(body);
                }
            }
        }


        public async Task<JObject> SendMiNAAsync(string path, IDictionary<string, string> form)
        {
            await EnsureServiceTokenAsync("micoapi").ConfigureAwait(false);
            var values = new Dictionary<string, string>(form);
            values["requestId"] = "app_ios_" + RandomString(30);
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api2.mina.mi.com" + path))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "MiHome/6.0.103 (com.xiaomi.mihome; build:6.0.103.1; iOS 14.4.0) Alamofire/6.0.103 MICO/iOSApp/appStore/6.0.103");
                request.Content = new FormUrlEncodedContent(values);
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var json = JObject.Parse(body);
                    if (json.Value<int?>("code") != 0)
                    {
                        throw new Exception("MiNA 请求失败：" + json.ToString());
                    }
                    return json;
                }
            }
        }

        public async Task<JObject> SendMiNAGetAsync(string path)
        {
            await EnsureServiceTokenAsync("micoapi").ConfigureAwait(false);
            var url = "https://api2.mina.mi.com" + path + (path.Contains("?") ? "&" : "?") + "requestId=app_ios_" + RandomString(30);
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "MiHome/6.0.103 (com.xiaomi.mihome; build:6.0.103.1; iOS 14.4.0) Alamofire/6.0.103 MICO/iOSApp/appStore/6.0.103");
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var json = JObject.Parse(body);
                    if (json.Value<int?>("code") != 0)
                    {
                        throw new Exception("MiNA 请求失败：" + json.ToString());
                    }
                    return json;
                }
            }
        }

        private void AddCookie(string url, string name, string value)
        {
            var uri = new Uri(url);
            try { _cookies.Add(uri, new Cookie(name, value, "/")); }
            catch { }
        }

        private string FindCookie(string url, string name)
        {
            try
            {
                var cookies = _cookies.GetCookies(new Uri(url));
                foreach (Cookie cookie in cookies)
                {
                    if (cookie.Name == name)
                    {
                        return cookie.Value;
                    }
                }
            }
            catch { }
            return null;
        }

        private static JObject ParseXiaomiJson(string body)
        {
            const string prefix = "&&&START&&&";
            if (body.StartsWith(prefix, StringComparison.Ordinal)) body = body.Substring(prefix.Length);
            return JObject.Parse(body);
        }

        private static byte[] Sha1(byte[] bytes) { using (var sha = SHA1.Create()) return sha.ComputeHash(bytes); }

        private static string CreateNonce()
        {
            var bytes = new byte[12];
            var random = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            Buffer.BlockCopy(random, 0, bytes, 0, 8);
            var minutes = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);
            bytes[8] = (byte)(minutes >> 24); bytes[9] = (byte)(minutes >> 16); bytes[10] = (byte)(minutes >> 8); bytes[11] = (byte)minutes;
            return Convert.ToBase64String(bytes);
        }

        private static string SignNonce(string ssecurity, string nonce)
        {
            var a = Convert.FromBase64String(ssecurity);
            var b = Convert.FromBase64String(nonce);
            var all = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, all, 0, a.Length); Buffer.BlockCopy(b, 0, all, a.Length, b.Length);
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(all));
        }

        private static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++) result.Append(chars[bytes[i] % chars.Length]);
            return result.ToString();
        }

        public void Dispose() { _http.Dispose(); }
    }
}
