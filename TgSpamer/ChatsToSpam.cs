using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TdLib;

namespace TgSpamer
{
    class ChatsToSpam : List<TdApi.Chat>
    {
        public const string ChatsToSpamFile = "chatsToSpam.txt";

        public ChatsToSpam(Dictionary<long, TdApi.Chat> chatIdToChat) : base()
        {
            if (File.Exists(ChatsToSpamFile))
                this.AddRange(File.ReadAllLines(ChatsToSpamFile, Encoding.UTF8).Select(l =>
                {
                    if (!long.TryParse(l.Trim(), out long id))
                        return chatIdToChat.Values.FirstOrDefault(ch => ch.Title.Equals(l, StringComparison.OrdinalIgnoreCase));

                    if (chatIdToChat.TryGetValue(id, out var chat))
                        return chat;

                    return null;
                }).Where(ch => ch != null));
            else
                File.WriteAllText(ChatsToSpamFile, "# Впишіть сюди ID або назви чатів з файлу chatIds.txt, в яких будемо постити");
        }
    }
}
