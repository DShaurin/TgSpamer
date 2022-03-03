using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TdLib;

namespace TgManager
{
    /// <summary>
    /// Telegram manager
    /// </summary>
    public class Manager : IDisposable
    {
        private readonly Action<string> _log;
        private readonly LoginParameters[] _loginParams;

        private readonly BlockingCollection<Tuple<string, TdApi.Message>> _messageQueue = new BlockingCollection<Tuple<string, TdApi.Message>>(128);
        private Task _eventInvoker;

        public Dictionary<long, TdApi.Chat> ChatIdToChat { get; } = new Dictionary<long, TdApi.Chat>();

        public Dictionary<long, TdApi.Supergroup> ChatIdToSupergroup { get; } = new Dictionary<long, TdApi.Supergroup>();

        public Dictionary<string, TdClient> Clients { get; } = new Dictionary<string, TdClient>();

        public bool Initialized { get; private set; } = false;

        public delegate void NewMessageHandler(string phone, TdApi.Message message);

        public event NewMessageHandler OnNewMessage;

        public Manager(LoginParameters[] loginParams, Action<string> log = null)
        {
            _log = log;
            _loginParams = loginParams;
        }

        public async Task Connect(Func<string, string> getConfirmationCode)
        {
            _eventInvoker = Task.Run(() =>
            {
                foreach (var msg in _messageQueue.GetConsumingEnumerable())
                    try
                    {
                        OnNewMessage?.Invoke(msg.Item1, msg.Item2);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText($"onNewMsg_exc_{DateTime.UtcNow:dd.MM.yy_HH.mm.ss}.txt", ex.ToString(), Encoding.UTF8);
                    }
            });

            foreach (var login in _loginParams)
            {
                if (!Directory.Exists(login.TgFolder))
                    Directory.CreateDirectory(login.TgFolder);

                var phone = login.PhoneNumber;
                var authNeeded = false;
                var resetEvent = new ManualResetEventSlim();
                var client = new TdClient();
                Clients[phone] = client;
                client.Bindings.SetLogVerbosityLevel(0);

                var tdLibParameters = new TdApi.TdlibParameters
                {
                    UseTestDc = false,
                    DatabaseDirectory = login.TgFolder,
                    FilesDirectory = login.TgFolder,
                    UseFileDatabase = false,
                    UseChatInfoDatabase = false,
                    UseMessageDatabase = false,
                    UseSecretChats = false,
                    ApiId = login.ApiId,
                    ApiHash = login.ApiHash,
                    ApplicationVersion = "1.7.0",
                    DeviceModel = "PC",
                    SystemLanguageCode = "en",
                    SystemVersion = "Win 10.0",
                    EnableStorageOptimizer = true,
                };

                client.UpdateReceived += async (sender, update) =>
                {
                    switch (update)
                    {
                        case TdApi.Update.UpdateAuthorizationState updateAuthorizationState when updateAuthorizationState.AuthorizationState.GetType() == typeof(TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters):
                            await client.ExecuteAsync(new TdApi.SetTdlibParameters { Parameters = tdLibParameters });
                            break;
                        case TdApi.Update.UpdateAuthorizationState updateAuthorizationState when updateAuthorizationState.AuthorizationState.GetType() == typeof(TdApi.AuthorizationState.AuthorizationStateWaitEncryptionKey):
                            await client.ExecuteAsync(new TdApi.CheckDatabaseEncryptionKey());
                            break;
                        case TdApi.Update.UpdateAuthorizationState updateAuthorizationState when updateAuthorizationState.AuthorizationState.GetType() == typeof(TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber):
                            authNeeded = true;
                            resetEvent.Set();
                            break;
                        case TdApi.Update.UpdateAuthorizationState updateAuthorizationState when updateAuthorizationState.AuthorizationState.GetType() == typeof(TdApi.AuthorizationState.AuthorizationStateWaitCode):
                            authNeeded = true;
                            resetEvent.Set();
                            break;
                        case TdApi.Update.UpdateUser updateUser:
                            resetEvent.Set();
                            break;
                        case TdApi.Update.UpdateConnectionState updateConnectionState:
                            _log?.Invoke($"Tg {phone} connection state: {updateConnectionState.State.GetType().Name}");
                            break;
                        case TdApi.Update.UpdateNewMessage updateNewMessage:
                            break;

                        default:
                            //_log?.Invoke($"Tg update: {update.GetType().Name}");
                            ; // add a breakpoint here to see other events
                            break;
                    }
                };

                resetEvent.Wait();
                if (authNeeded)
                {
                    await client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
                    {
                        PhoneNumber = phone
                    });
                    await client.ExecuteAsync(new TdApi.CheckAuthenticationCode
                    {
                        Code = getConfirmationCode(phone)
                    });
                }

                foreach (var chat in GetChannels(client))
                {
                    ChatIdToChat[chat.Id] = chat;
                    if (chat.Type is TdApi.ChatType.ChatTypeSupergroup sgc)
                        ChatIdToSupergroup[chat.Id] = client.GetSupergroupAsync(sgc.SupergroupId).GetAwaiter().GetResult();
                }
            }

            Initialized = true;
        }

        public static IEnumerable<TdApi.Chat> GetChannels(TdClient client, TdApi.ChatList chatList = null, int limit = 1000)
        {
            var chats = client.GetChatsAsync(chatList ?? new TdApi.ChatList.ChatListMain(), limit).GetAwaiter().GetResult();
            foreach (var chatId in chats.ChatIds)
            {
                var chat = client.GetChatAsync(chatId).GetAwaiter().GetResult();
                if (chat.Type is TdApi.ChatType.ChatTypeSupergroup || chat.Type is TdApi.ChatType.ChatTypeBasicGroup || chat.Type is TdApi.ChatType.ChatTypePrivate)
                    yield return chat;
            }
        }

        public void Dispose()
        {
            foreach (var client in Clients)
                client.Value.Dispose();
            _messageQueue.CompleteAdding();
            _eventInvoker?.Wait();
        }
    }
}
