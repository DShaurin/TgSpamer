using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TdLib;
using System.Diagnostics;

namespace TgSpamer
{
    class Program
    {
        private const string TgLoginFile = "TgLogin.json";

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

            File.WriteAllLines("chatIds.txt", manager.ChatIdToChat.Values.Select(c => $"{c.Id} \t{c.Title}"), Encoding.UTF8);
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Connected {manager.Clients.Count} telegrams: {string.Join(", ", manager.Clients.Select(c => c.Key))}");

            var chatsToSpam = new ChatsToSpam(manager.ChatIdToChat);
            var messages = MessagesToSend.ReadMessages();

            Console.WriteLine($"Spaming to {chatsToSpam.Count} chats with {messages.Count} messages");

            Console.WriteLine("Press enter to stop.");

            var rnd = new Random();
            var timer = new Timer(t =>
            {
                try
                {
                    foreach (var chat in chatsToSpam.OrderBy(_ => rnd.NextDouble()))
                    {
                        var client = manager.Clients.OrderBy(_ => rnd.NextDouble()).First();
                        var chats = TgManager.Manager.GetChannels(client.Value).ToArray();
                        if (!chats.Any(c => c.Id == chat.Id))
                        { // Need to join
                            try
                            {
                                if (manager.ChatIdToSupergroup.TryGetValue(chat.Id, out var sg))
                                {
                                    var pubChat = client.Value.SearchPublicChatAsync(sg.Username).GetAwaiter().GetResult();
                                    client.Value.OpenChatAsync(pubChat.Id).GetAwaiter().GetResult();
                                    client.Value.JoinChatAsync(pubChat.Id).GetAwaiter().GetResult();
                                }
                            }
                            catch (Exception ex)
                            {
                                logit($"Client {client.Key} failed to join chat {chat.Id} {chat.Title}: {ex}");
                                Console.WriteLine($"{DateTime.UtcNow:d/M/y H:mm:ss} Client {client.Key} failed to join chat {chat.Id} {chat.Title}: {ex.Message}");
                            }
                            continue;
                        }

                        const int MessagesCount = 200;
                        var history = new List<TdApi.Message>(MessagesCount + 1);

                        long? msgThreadId = null;
                        TdApi.MessageThreadInfo thread = null;
                        TdApi.Messages msgs = client.Value.GetChatHistoryAsync(chat.Id, limit: MessagesCount, onlyLocal: false).GetAwaiter().GetResult();
                        if (msgs.Messages_.Length > 0 && !chat.Permissions.CanSendMessages)
                        {
                            var lastMsg = msgs.Messages_.OrderByDescending(m => m.Date).First();
                            try
                            {
                                thread = client.Value.GetMessageThreadAsync(lastMsg.ChatId, lastMsg.Id).GetAwaiter().GetResult();
                                if (thread?.MessageThreadId != 0 && thread.ChatId != chat.Id)
                                {
                                    var threadChat = client.Value.GetChatAsync(thread.ChatId).GetAwaiter().GetResult();
                                    if (threadChat.Permissions.CanSendMessages)
                                    {
                                        msgThreadId = lastMsg.Id;
                                        msgs = null;
                                    }
                                    else
                                        thread = null;
                                }
                                else
                                    thread = null;
                            }
                            catch { }
                        }
                        if (!msgThreadId.HasValue)
                            history.AddRange(msgs.Messages_);

                        while (history.Count < MessagesCount)
                        {
                            if (thread != null)
                                msgs = client.Value.GetMessageThreadHistoryAsync(thread.ChatId, thread.MessageThreadId, fromMessageId: msgs?.Messages_.Last().Id ?? 0, limit: MessagesCount).GetAwaiter().GetResult();
                            else
                                msgs = client.Value.GetChatHistoryAsync(chat.Id, fromMessageId: msgs?.Messages_.Last().Id ?? 0, limit: MessagesCount, onlyLocal: true).GetAwaiter().GetResult();
                            if (msgs.TotalCount == 0)
                                break;
                            history.AddRange(msgs.Messages_);
                        }

                        if (history.Count < 5 || (thread == null && !chat.Permissions.CanSendMessages))
                            continue;

                        var now = DateTime.UtcNow;
                        Console.WriteLine($"{now:d/M/y H:mm:ss} Tg {client.Key} got {history.Count} messages from {chat.Id} '{chat.Title}'");

                        var ourLastMsgTime = DateTime.MinValue.AddDays(2);
                        var ourLastMsgTimeInt = history.Where(h => h.Sender is TdApi.MessageSender.MessageSenderUser sender && manager.Clients.Any(c => c.Value.GetMeAsync().GetAwaiter().GetResult().Id == sender.UserId))
                            .Select(h => h.Date).OrderByDescending(d => d).FirstOrDefault();
                        if (ourLastMsgTimeInt > 0)
                            ourLastMsgTime = DateTimeOffset.FromUnixTimeSeconds(ourLastMsgTimeInt).UtcDateTime;

                        if (now.Subtract(ourLastMsgTime).TotalHours > 6.5)
                        {
                            // Let's post again!
                            var text = messages[rnd.Next(0, messages.Count)];
                            if (thread != null)
                                client.Value.SendMessageAsync(chatId: thread.ChatId,
                                    messageThreadId: thread.MessageThreadId,
                                    inputMessageContent: text);
                            else
                                client.Value.SendMessageAsync(chatId: chat.Id,
                                    inputMessageContent: text);

                            logit($"Posted message from {client.Value} in {chat.Id} {chat.Title}");
                            Console.WriteLine($"{now:d/M/y H:mm:ss} Posted message from {client.Value} in {chat.Id} {chat.Title}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logit($"Error occured: {ex}");
                    Console.WriteLine($"{DateTime.UtcNow:d/M/y H:mm:ss} Error: {ex.GetType().Name} {ex.Message} @ {ex.StackTrace.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}");
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(3));

            Console.ReadLine();
        }
    }
}
