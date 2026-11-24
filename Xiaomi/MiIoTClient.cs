using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiSound.AI.Local;
using MiSound.AI.Local.Models;
using Newtonsoft.Json.Linq;

namespace MiSound.AI.Local.Xiaomi
{
    public sealed class MiIoTClient
    {
        private readonly XiaomiAccount _account;
        public MiIoTClient(XiaomiAccount account) { _account = account; }

        public async Task<List<XiaomiDevice>> GetDevicesAsync()
        {
            var data = new JObject { ["getVirtualModel"] = false, ["getHuamiDevices"] = 0 };
            var response = await _account.SendMiIoTAsync("/home/device_list", data).ConfigureAwait(false);
            var list = response["result"] == null ? null : response["result"]["list"] as JArray;
            var devices = new List<XiaomiDevice>();
            if (list == null) return devices;
            foreach (JToken item in list)
            {
                devices.Add(new XiaomiDevice
                {
                    Name = (string)item["name"],
                    Model = (string)item["model"],
                    Did = (string)item["did"],
                    IsOnline = item["isOnline"] == null ? (bool?)null : (bool?)item["isOnline"]
                });
            }
            return devices;
        }
    }
}
