using Newtonsoft.Json;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using TdLib;
using System.Collections.Generic;

namespace TgSearch
{
    class Program
    {
        private const string TgLoginFile = "TgLogin.json";
        private const string ChannelsFile = "Channels.txt";
        private const string KeywordsFile = "Keywords.txt";
        private const int MessagesCount = 400;

        private const string ResultsFile = "results.txt";

        private static readonly Lazy<TgManager.LoginParameters[]> loginParams = new(() =>
        {
            if (File.Exists(TgLoginFile))
                return JsonConvert.DeserializeObject<TgManager.LoginParameters[]>(File.ReadAllText(TgLoginFile, Encoding.UTF8));
            var newParams = new[] { new TgManager.LoginParameters() };
            File.WriteAllText(TgLoginFile, JsonConvert.SerializeObject(newParams, Formatting.Indented), Encoding.UTF8);
            return newParams;
        });

        static void Main(string[] args)
        {
            using var log = new StreamWriter("log.txt", true, Encoding.UTF8) { AutoFlush = true };
            void logit(string s)
            {
                lock (log)
                    log.WriteLine($"{DateTime.UtcNow:d/M/y H:mm:ss} [{Thread.CurrentThread.ManagedThreadId}] {s}");
            }

            using var manager = new TgManager.Manager(loginParams.Value, logit);
            manager.Connect(phone =>
            {
                Console.WriteLine($"Enter confirmation code from {phone} Telegram app:");
                var code = Console.ReadLine();
                return code;
            }).GetAwaiter().GetResult();

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Connected {manager.Clients.Count} telegrams: {string.Join(", ", manager.Clients.Select(c => c.Key))}");

            var channels = File.ReadAllLines(ChannelsFile, Encoding.UTF8).Select(l => l.Replace("https://t.me/", "", StringComparison.OrdinalIgnoreCase)).ToArray();
            var keywords = File.ReadAllLines(KeywordsFile, Encoding.UTF8);

            var client = manager.Clients.First();

            using var result = new StreamWriter(ResultsFile, false, Encoding.UTF8) { AutoFlush = true };
            foreach (var link in channels)
            {
                try
                {
                    long chatId = 0;
                    var sgroup = manager.ChatIdToSupergroup.Values.FirstOrDefault(sg => sg.Username.Equals(link, StringComparison.OrdinalIgnoreCase));
                    if (sgroup == null)
                    { // Need to join
                        try
                        {
                            var pubChat = client.Value.SearchPublicChatAsync(link).GetAwaiter().GetResult();
                            client.Value.OpenChatAsync(pubChat.Id).GetAwaiter().GetResult();
                            client.Value.JoinChatAsync(pubChat.Id).GetAwaiter().GetResult();
                            chatId = pubChat.Id;
                            manager.ChatIdToChat[pubChat.Id] = pubChat;
                            if (pubChat.Type is TdApi.ChatType.ChatTypeSupergroup sgc)
                                manager.ChatIdToSupergroup[pubChat.Id] = client.Value.GetSupergroupAsync(sgc.SupergroupId).GetAwaiter().GetResult();
                            Thread.Sleep(1000);
                        }
                        catch (Exception ex)
                        {
                            logit($"Client {client.Key} failed to join chat {link}: {ex}");
                            Console.WriteLine($"{DateTime.Now:d/M/y H:mm:ss} Client {client.Key} failed to join chat {link}: {ex.Message}");
                            continue;
                        }
                    }
                    else
                        chatId = manager.ChatIdToSupergroup.First(kv => kv.Value.Id == sgroup.Id).Key;

                    var history = new List<TdApi.Message>(MessagesCount + 1);

                    TdApi.Messages msgs = client.Value.GetChatHistoryAsync(chatId, limit: MessagesCount, onlyLocal: false).GetAwaiter().GetResult();
                    while (history.Count < MessagesCount)
                    {
                        Thread.Sleep(1000);
                        msgs = client.Value.GetChatHistoryAsync(chatId, fromMessageId: msgs?.Messages_.Last().Id ?? 0, limit: MessagesCount, onlyLocal: false).GetAwaiter().GetResult();
                        if (msgs.TotalCount == 0)
                            break;
                        history.AddRange(msgs.Messages_);
                    }

                    foreach (var msg in history)
                    {
                        string text = null;
                        switch (msg.Content)
                        {
                            case TdApi.MessageContent.MessageText textMsg:
                                text = textMsg.Text.Text;
                                break;
                            case TdApi.MessageContent.MessagePhoto photoMsg:
                                text = photoMsg.Caption.Text;
                                break;
                            case TdApi.MessageContent.MessagePoll pollMsg:
                                text = pollMsg.Poll.Question;
                                break;
                            case TdApi.MessageContent.MessageDocument docMsg:
                                text = docMsg.Caption.Text;
                                break;
                        }

                        if (text == null) continue;
                        var kword = keywords.FirstOrDefault(kw => text.Contains(kw, StringComparison.OrdinalIgnoreCase));
                        if (kword != null)
                            result.WriteLine($"Chat {link} '{manager.ChatIdToChat[chatId].Title}'\n{kword}\n{DateTimeOffset.FromUnixTimeSeconds(msg.Date).DateTime}\n'{text}'\n");
                    }
                }
                catch (Exception ex)
                {
                    logit($"Error searching messages in {link}: {ex}");
                    Console.WriteLine($"{DateTime.Now:d/M/y H:mm:ss} Error searching messages in {link}: {ex.Message}");
                }
            }
        }
    }
}
