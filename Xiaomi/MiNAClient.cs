using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiSound.AI.Local.Models;
using Newtonsoft.Json.Linq;

namespace MiSound.AI.Local.Xiaomi
{
    public sealed class MiNAClient
    {
        private readonly XiaomiAccount _account;
        public MiNAClient(XiaomiAccount account) { _account = account; }

        public async Task<List<XiaomiDevice>> GetDevicesAsync()
        {
            var response = await _account.SendMiNAGetAsync("/admin/v2/device_list?master=0").ConfigureAwait(false);
            var list = response["data"] as JArray;
            var devices = new List<XiaomiDevice>();
            if (list == null) return devices;
            foreach (JToken item in list)
            {
                devices.Add(new XiaomiDevice
                {
                    Name = (string)item["name"],
                    Model = (string)item["hardware"],
                    Hardware = (string)item["hardware"],
                    DeviceId = (string)item["deviceID"],
                    Did = (string)item["miotDID"]
                });
            }
            return devices;
        }
        /// <summary>
        /// 设置音量
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="volume"></param>
        /// <returns></returns>
        public async Task<JObject> SetVolumeAsync(string deviceId, int volume)
        {
            var form = new Dictionary<string, string>
            {
                { "deviceId", deviceId },
                { "message", "{\"volume\":" + volume + ",\"media\":\"app_ios\"}" },
                { "method", "player_set_volume" },
                { "path", "mediaplayer" }
            };
            var rs = await _account.SendMiNAAsync("/remote/ubus", form).ConfigureAwait(false);
            Console.WriteLine("SetVolumeAsync:" + rs.ToString());
            return rs;
        }
        /// <summary>
        /// 发送语音
        /// </summary>
        /// <param name="miotDid"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<bool> SpeakMiotAsync(string miotDid, string text)
        {
            var data = new JObject
            {
                ["params"] = new JObject
                {
                    ["did"] = miotDid,
                    ["siid"] = 5,
                    ["aiid"] = 3,
                    ["in"] = new JArray { text }
                }
            };
            var result = await _account.SendMiIoTAsync("/v2/miotspec/action", data).ConfigureAwait(false);
            //Console.WriteLine("SpeakMiotAsync: " + result.ToString());
            return result.Value<int?>("code") == 0;
        }
        /// <summary>
        /// 发送语音(不在使用版本)
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<bool> SpeakAsync(string deviceId, string text)
        {


            var message = new JObject { ["text"] = text }.ToString(Newtonsoft.Json.Formatting.None);
            var form = new Dictionary<string, string>
            {
                { "deviceId", deviceId },
                { "message", message },
                { "method", "text_to_speech" },
                { "path", "mibrain" },
            };
            var response = await _account.SendMiNAAsync("/remote/ubus", form).ConfigureAwait(false);
            Console.WriteLine("SpeakAsync:" + response.ToString());
            return response.Value<int?>("code") == 0;
        }

        /// <summary>
        /// 获取最后会话
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="hardware"></param>
        /// <returns></returns>
        public async Task<List<RecognizedMessage>> GetLatestAskAsync(string deviceId, string hardware)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var response = await _account.GetConversationAsync(deviceId, hardware, timestamp, 10).ConfigureAwait(false);

            //Console.WriteLine("GetLatestAskAsync: " + response.ToString());

            var messages = new List<RecognizedMessage>();

            JToken dataToken = response["data"];
            JObject data = null;

            // 小米接口的 data 通常是 JSON 字符串
            if (dataToken != null && dataToken.Type == JTokenType.String)
            {
                string json = (string)dataToken;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    data = JObject.Parse(json);
                }
            }
            else
            {
                data = dataToken as JObject;
            }

            if (data == null)
            {
                return messages;
            }

            var records = data["records"] as JArray;
            if (records == null)
            {
                return messages;
            }

            foreach (JToken item in records)
            {
                string question = (string)item["query"];

                if (string.IsNullOrWhiteSpace(question))
                {
                    continue;
                }

                long time = (long?)item["time"] ?? 0;

                messages.Add(new RecognizedMessage
                {
                    RequestId = time.ToString(),
                    Question = question,
                    Timestamp = time
                });
            }

            return messages;
        }

    }

    public class RecognizedMessage
    {
        public string RequestId { get; set; }
        public string Question { get; set; }
        public long Timestamp { get; set; }
    }
}
