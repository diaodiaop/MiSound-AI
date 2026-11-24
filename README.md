# MiSound-AI


基于c#的小米音响接入第三方AI  (本代码都由chatgpt网页版生成,自己一行没写)


# MiSound-AI.Local（.NET Framework 4.8）

这是一个本地控制台测试程序。程序在 Windows 本机运行，主动访问小米云端接口和你配置的 AI API；不需要公网服务器，也不需要音箱访问本机端口。

## 当前包含的三项测试

1. **MIoT 设备列表**：使用 `userId + passToken` 完成小米 Passport 的服务认证，再请求 `/home/device_list`。
2. **MiNA 音箱列表 + TTS**：请求 `/admin/v2/device_list`，然后通过 `/remote/ubus` 调用 `text_to_speech`。
3. **语音监听 + AI + 播报**：循环调用 `nlp_result_get` 获取小爱云端识别结果，调用 OpenAI 兼容的 `/chat/completions`，再调用 `text_to_speech` 播报。

> 重要限制：小米这些是非公开接口，可能变更；`nlp_result_get` 并非所有音箱型号/固件都支持。若音箱列表和 TTS 成功，但监听一直没有语音结果，可能是型号不支持该 ubus 方法，需要按具体型号改用对话记录接口。此版本先把常见 MiService 路径做成第一轮验证版。

## 运行


1. Windows 安装 .NET Framework 4.8 Developer Pack（或 Visual Studio 中安装 .NET Framework 4.8 targeting pack）。
2. 用 Visual Studio 打开 `MiSound.AI.Local.sln`。
3. 还原 NuGet 包（项目依赖 Newtonsoft.Json 13.0.3）。
4. 修改项目目录下的 `config.json`：

```json
{
  "Xiaomi": {
    "UserId": "小米账号数字ID",
    "PassToken": "小米passToken",
    "MiNADeviceId": "",
    "PollIntervalSeconds": 3,
    "DeviceId": "MiNA设备deviceID或miotDID"
  },
  "AI": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "你的API Key",
    "Model": "gpt-4o-mini",
    "SystemPrompt": "你是一个中文语音助手。回答自然、简洁，适合通过智能音箱朗读。不要使用Markdown表格。"
  }
}
```

5. 按 F5 启动。

菜单：
- `1` 获取米家设备列表；
- `2` 获取小爱音箱列表，输入文字测试播报；
- `3` 监听语音并转 AI 回答，按 Ctrl+C 停止。

## 配置说明

- `UserId` 是小米账号数字 ID，不是手机号/邮箱。
- `PassToken` 是账号登录凭证。不要把 config.json 发给别人，不要提交到 Git，也不要把日志公开。
- `MiNADeviceId` 留空时程序会让你选择设备；也可以填写 MiNA 列表中的 `deviceID` 或 `miotDID`。
- `BaseUrl` 填 OpenAI 兼容 API 的根地址，例如 `https://api.openai.com/v1` 或你的中转站 `/v1` 地址。
- `ApiKey` 是 AI 服务的 Key，与小米 passToken 无关。

## 安全建议

- 建议先使用专门用于测试的小米账号，并确认账号凭证仅保存在自己的电脑。
- `config.json` 包含 passToken 和 AI Key，妥善保管；测试结束后可清空 passToken。
- 程序不会主动打印 passToken、serviceToken 或 API Key。



本项目是独立的 C# 学习验证实现，不是小米官方软件，也不保证所有型号/地区/固件均兼容。
