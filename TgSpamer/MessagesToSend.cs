using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TdLib;

namespace TgSpamer
{
    class MessagesToSend : List<TdApi.InputMessageContent>
    {
        private const string MessagesFile = "messagesToSend.json";

        public static List<TdApi.InputMessageContent> ReadMessages(string file = MessagesFile)
        {
            if (!File.Exists(file))
                File.WriteAllText(file,
                    JsonConvert.SerializeObject(new List<TdApi.InputMessageContent> { new TdApi.InputMessageContent.InputMessageText { Text = new TdApi.FormattedText { Text = "Слава Україні! Перевіряю бота, не зважайте.\nЗараз Видалю" } } },
                    Formatting.Indented,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }));

            return JsonConvert.DeserializeObject<List<TdApi.InputMessageContent>>(File.ReadAllText(file, Encoding.UTF8),
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
        }

        public string Serialize()
            => JsonConvert.SerializeObject(this, Formatting.Indented);

        public void WriteToFile(string file = MessagesFile)
            => File.WriteAllText(file, Serialize(), Encoding.UTF8);
    }
}
