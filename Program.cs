using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiSound.AI.Local;
using MiSound.AI.Local.Models;
using MiSound.AI.Local.Xiaomi;

namespace MiSound.AI.Local
{
    internal class Program
    {
        private static AppConfig _config;
        private static XiaomiAccount _account;
        private static MiIoTClient _miot;
        private static MiNAClient _mina;
        private static OpenAIClient _ai;
        private static List<XiaomiDevice> _minaDevices;

        private static void Main(string[] args)
        {



            try { MainAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("程序异常：" + ex.Message);
                Console.ResetColor();
                Console.WriteLine("按回车退出。");
                Console.ReadLine();
            }
        }

        private static async Task MainAsync()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "MiSound.AI.Local - 小米音箱 AI 本地测试";
            _config = AppConfig.Load("config.json");
            _account = new XiaomiAccount(_config.Xiaomi.UserId, _config.Xiaomi.PassToken, _config.Xiaomi.DeviceId);
            _miot = new MiIoTClient(_account);
            _mina = new MiNAClient(_account);
            _ai = new OpenAIClient(_config.AI.BaseUrl, _config.AI.ApiKey, _config.AI.Model, _config.AI.SystemPrompt);

            Console.WriteLine("====================================================");
            Console.WriteLine(" MiSound.AI.Local - .NET Framework 4.8");
            Console.WriteLine(" 本程序在本机运行，主动连接小米云端和 AI API");
            Console.WriteLine("====================================================");
            Console.WriteLine("提示：passToken 是账号登录凭证，请勿分享 config.json 或运行日志。\r\n");



            Console.ForegroundColor = ConsoleColor.Green;


            Console.WriteLine("正在进行AI功能测试,请稍后......");
            await _ai.AskAsync("你好,请问你是什么模型", (s) =>
            {
                Console.WriteLine("测试AI返回：" + s);
            });

            Console.ResetColor();

            while (true)
            {
                Console.WriteLine("请选择功能：");
                Console.WriteLine("  1. 获取米家设备列表（MIoT）");
                Console.WriteLine("  2. 获取小爱音箱列表并测试播报（MiNA TTS）");
                Console.WriteLine("  3. 开始监听语音 -> AI 对话 -> 音箱播报");
                Console.WriteLine("  0. 退出");
                Console.Write("请输入：");
                var choice = Console.ReadLine();
                if (choice == "0")
                {
                    break;
                }
                try
                {
                    if (choice == "1")
                    {
                        await ShowMiotDevicesAsync();
                    }
                    else if (choice == "2")
                    {
                        await TestTtsAsync();
                    }
                    else if (choice == "3")
                    {
                        await ListenAndReplyAsync();
                    }
                    else
                    {
                        Console.WriteLine("无效选项。\r\n");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("操作失败：" + ex.Message);
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
            _account.Dispose();
        }

        private static async Task ShowMiotDevicesAsync()
        {
            Console.WriteLine("\r\n正在请求小米云端设备列表...");
            var devices = await _miot.GetDevicesAsync();
            Console.WriteLine("获取成功，设备数量：" + devices.Count);
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                Console.WriteLine("[{0}] {1} | {2} | DID={3} | Online={4}", i + 1, d.Name, d.Model, d.Did, d.IsOnline.HasValue ? d.IsOnline.Value.ToString() : "未知");
            }
            if (devices.Count == 0)
            {
                Console.WriteLine("没有返回设备。请检查账号地区、认证状态或接口是否变化。");
            }
            Console.WriteLine();
        }

        private static async Task<List<XiaomiDevice>> LoadMiNADevicesAsync()
        {
            Console.WriteLine("\r\n正在请求小爱音箱设备列表...");
            var devices = await _mina.GetDevicesAsync();
            _minaDevices = devices;
            Console.WriteLine("获取成功，音箱数量：" + devices.Count);
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                Console.WriteLine("[{0}] {1} | hardware={2} | deviceID={3} | miotDID={4}", i + 1, d.Name, d.Hardware, d.DeviceId, d.Did);
            }
            if (devices.Count == 0)
            {
                Console.WriteLine("MiNA 没有返回音箱设备。请检查该账号下是否绑定小爱音箱。");
            }
            return devices;
        }

        private static async Task TestTtsAsync()
        {
            var devices = await LoadMiNADevicesAsync();
            if (devices.Count == 0)
            {
                return;
            }
            var device = SelectSpeaker(devices);
            if (device == null)
            {
                return;
            }
            Console.Write("请输入要让音箱播报的文字：");
            var text = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            Console.WriteLine("正在发送 TTS...");
            var vo = await _mina.SetVolumeAsync(device.DeviceId, 100);


            var ok = await _mina.SpeakMiotAsync(device.Did, text);
            Console.WriteLine(ok ? "TTS 指令已被小米云端接受，请听音箱。\r\n" : "TTS 返回失败。\r\n");
        }

        private static XiaomiDevice SelectSpeaker(List<XiaomiDevice> devices)
        {
            if (!string.IsNullOrWhiteSpace(_config.Xiaomi.MiNADeviceId))
            {
                var configured = devices.FirstOrDefault(x => x.DeviceId == _config.Xiaomi.MiNADeviceId || x.Did == _config.Xiaomi.MiNADeviceId);
                if (configured != null)
                {
                    return configured;
                }
                Console.WriteLine("config.json 中的 MiNADeviceId 未匹配到设备，将提示选择。");
            }
            Console.Write("选择音箱序号（直接回车选择第一个）：");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return devices[0];
            }
            int index;
            if (int.TryParse(input, out index) && index >= 1 && index <= devices.Count) return devices[index - 1];
            {
                Console.WriteLine("序号无效。");
            }
            return null;
        }

        private static async Task ListenAndReplyAsync()
        {
            var devices = await LoadMiNADevicesAsync();
            if (devices.Count == 0)
            {
                return;
            }
            var device = SelectSpeaker(devices);
            if (device == null)
            {
                return;
            }

            Console.WriteLine("\r\n正在初始化语音监听。先读取一次现有记录，避免把历史问题重复回答...");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var initial = await _mina.GetLatestAskAsync(device.DeviceId, device.Hardware);
                if (initial.Any())
                {
                    foreach (var m in initial)
                    {
                        seen.Add(GetMessageKey(m));
                    }
                    Console.WriteLine("初始化完成,当前读取到 " + initial.Count() + " 条记录。现在对着音箱说话；按 Ctrl+C 结束监听。");
                }
                else
                {
                    Console.WriteLine("初始化可能失败,因为没有读取到任何记录。");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("首次读取语音记录失败：" + ex.Message);
                Console.WriteLine("当前音箱固件可能不支持 nlp_result_get，后续仍会继续尝试。");
            }

            while (true)
            {
                try
                {
                    var messages = await _mina.GetLatestAskAsync(device.DeviceId, device.Hardware);
                    foreach (var message in messages)
                    {
                        var key = GetMessageKey(message);
                        if (!seen.Add(key))
                        {
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(message.Question))
                        {
                            continue;
                        }
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("\r\n[识别到语音] " + message.Question);
                        Console.ResetColor();
                        var answer = await _ai.AskAsync(message.Question);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[AI 回答] " + answer);
                        Console.ResetColor();
                        var spoken = await _mina.SpeakMiotAsync(device.Did, answer);
                        Console.WriteLine(spoken ? "[音箱] 播报指令已发送。" : "[音箱] 播报失败。");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[轮询异常] " + ex.Message);
                }
                await Task.Delay(Math.Max(2, _config.Xiaomi.PollIntervalSeconds) * 1000);
            }
        }

        private static string GetMessageKey(RecognizedMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.RequestId))
            {
                return message.RequestId;
            }
            return (message.Timestamp.ToString() + "|" + message.Question);
        }
    }
}
