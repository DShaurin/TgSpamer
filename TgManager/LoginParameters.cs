using System;
using System.IO;
using Newtonsoft.Json;

namespace TgManager
{
    public class LoginParameters
    {
        [JsonProperty(Required = Required.Default)]
        public string TgFolder { get; private set; } = Path.Combine(Environment.CurrentDirectory, "tg");

        [JsonProperty(Required = Required.Always)]
        public int ApiId { get; private set; } = 0;

        [JsonProperty(Required = Required.Always)]
        public string ApiHash { get; private set; } = "api hash";

        [JsonProperty(Required = Required.Always)]
        public string PhoneNumber { get; private set; } = "+380987654321";
    }
}
