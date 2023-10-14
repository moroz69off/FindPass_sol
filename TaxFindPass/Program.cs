using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TdLib;
using TdLib.Bindings;

namespace TaxFindPass
{
    internal class Program
    {
        private static TdClient _client;

        // Debug mode======================================================
        private static int ApiId = 1962340;
        private static string ApiHash = "658bcc68acc90d5ba4ed5ca5d7b83ed3";
        private static string PhoneNumber = "+79525639039";
        // ======================================================Debug mode

        private static string ApplicationVersion = "0.0.0";

        private static bool _authNeeded;
        private static bool _passwordNeeded;

        private static readonly ManualResetEventSlim ReadyToAuthenticate = new();

        private static async Task Main()
        {
            // Создаём Telegram-клиент и ставим Fatal (минимальный уровень), так как много логов нам не нужно :)
            _client = new TdClient();
            _client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);

            _client.UpdateReceived += async (_, update) => { await ProcessUpdates(update); };

            ReadyToAuthenticate.Wait();

            if (_authNeeded)
            {
                // Interactively handling authentication
                await HandleAuthentication();
            }

            // Querying info about current user and some channels
            TdApi.User currentUser = await GetCurrentUser();

            string fullUserName = $"{currentUser.FirstName} {currentUser.LastName}".Trim();
            Console.WriteLine($"Successfully logged in as [{currentUser.Id}] / [@{currentUser.Usernames?.ActiveUsernames[0]}] / [{fullUserName}]");

            const int channelLimit = 5;
            var channels = GetChannels(channelLimit);
            Console.WriteLine($"Top {channelLimit} channels:");

            await foreach (var channel in channels)
            {
                Console.WriteLine($"[{channel.Id}] -> [{channel.Title}] ({channel.UnreadCount} messages unread)");
            }

            Console.WriteLine("Press ENTER to exit from application");
            Console.ReadLine();
        }

        private static async IAsyncEnumerable<TdApi.Chat> GetChannels(int limit)
        {
            TdApi.Chats chats = await _client.ExecuteAsync(new TdApi.GetChats { Limit = limit });

            foreach (var chatId in chats.ChatIds)
            {
                TdApi.Chat chat = await _client.ExecuteAsync(new TdApi.GetChat { ChatId = chatId });

                if (chat.Type is TdApi.ChatType.ChatTypeSupergroup or TdApi.ChatType.ChatTypeBasicGroup or TdApi.ChatType.ChatTypePrivate)
                {
                    yield return chat;
                }
            }
        }

        private static async Task<TdApi.User> GetCurrentUser()
        {
            return await _client.ExecuteAsync(new TdApi.GetMe());
        }

        private static async Task HandleAuthentication()
        {
            // Setting phone number
            await _client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
            {
                PhoneNumber = PhoneNumber
            });

            // Telegram servers will send code to us
            Console.Write("Insert the login code: ");
            var code = Console.ReadLine();

            await _client.ExecuteAsync(new TdApi.CheckAuthenticationCode
            {
                Code = code
            });

            if (!_passwordNeeded) { return; }

            // 2FA may be enabled. Cloud password is required in that case.
            Console.Write("Insert the password: ");
            var password = Console.ReadLine();

            await _client.ExecuteAsync(new TdApi.CheckAuthenticationPassword
            {
                Password = password
            });
        }

        private static async Task ProcessUpdates(TdApi.Update update)
        {
            // Since Tdlib was made to be used in GUI application we need to struggle a bit and catch required events to determine our state.
            // Below you can find example of simple authentication handling.
            // Please note that AuthorizationStateWaitOtherDeviceConfirmation is not implemented.

            switch (update)
            {
                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters }:
                    // TdLib creates database in the current directory.
                    // so create separate directory and switch to that dir.
                    var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                    await _client.ExecuteAsync(new TdApi.SetTdlibParameters
                    {
                        ApiId = ApiId,
                        ApiHash = ApiHash,
                        DeviceModel = "PC",
                        SystemLanguageCode = "ru",
                        ApplicationVersion = ApplicationVersion,
                        DatabaseDirectory = filesLocation,
                        FilesDirectory = filesLocation,
                        // More parameters available!
                    });
                    break;

                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber }:
                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitCode }:
                    _authNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPassword }:
                    _authNeeded = true;
                    _passwordNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateUser:
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateConnectionState { State: TdApi.ConnectionState.ConnectionStateReady }:
                    // You may trigger additional event on connection state change
                    break;

                default:
                    // ReSharper disable once EmptyStatement
                    ;
                    // Add a breakpoint here to see other events
                    break;
            }
        }
    }
}