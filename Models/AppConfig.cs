using System;
using System.IO;
using Newtonsoft.Json;

namespace MiSound.AI.Local.Models
{
    public class AppConfig
    {
        public XiaomiConfig Xiaomi { get; set; }
        public AIConfig AI { get; set; }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到配置文件：" + path);
            var config = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(path));
            if (config == null || config.Xiaomi == null || config.AI == null)
                throw new Exception("config.json 格式不正确，需要 Xiaomi 和 AI 节点。");
            if (string.IsNullOrWhiteSpace(config.Xiaomi.UserId) || config.Xiaomi.UserId.StartsWith("填写"))
                throw new Exception("请先在 config.json 中填写 Xiaomi.UserId。");
            if (string.IsNullOrWhiteSpace(config.Xiaomi.PassToken) || config.Xiaomi.PassToken.StartsWith("填写"))
                throw new Exception("请先在 config.json 中填写 Xiaomi.PassToken。");
            if (config.Xiaomi.PollIntervalSeconds < 1) config.Xiaomi.PollIntervalSeconds = 3;
            return config;
        }
    }

    public class XiaomiConfig
    {
        public string UserId { get; set; }
        public string PassToken { get; set; }
        public string MiNADeviceId { get; set; }
        public int PollIntervalSeconds { get; set; }
        public string DeviceId { get; set; }
    }

    public class AIConfig
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string SystemPrompt { get; set; }
    }
}
