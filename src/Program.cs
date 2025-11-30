using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static ulong TargetChannelId = 123456789000000000;

        public static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN env missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Log += msg =>
            {
                Console.WriteLine(msg.ToString());
                return Task.CompletedTask;
            };

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(
                    new Game("Galactic Deobfuscation Service Free", ActivityType.Playing)
                );
                Console.WriteLine("Bot is ready");
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            if (message.Attachments.Count == 0)
                return;

            bool inChannel = message.Channel.Id == TargetChannelId;
            bool inDM = message.Channel is SocketDMChannel;

            if (!inChannel && !inDM)
                return;

            var file = message.Attachments.First();
            
            if (!file.Filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync("Please upload a valid .lua file");
                return;
            }

            string tempInput = null;
            string tempOutput = null;

            try
            {
                await message.Channel.SendMessageAsync("Processing your file");

                tempInput = Path.GetTempFileName();
                tempOutput = Path.GetTempFileName() + ".lua";

                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(file.Url);
                    await File.WriteAllBytesAsync(tempInput, data);
                }

                var fileContent = await File.ReadAllTextAsync(tempInput);
                
                if (string.IsNullOrWhiteSpace(fileContent))
                {
                    await message.Channel.SendMessageAsync("File is empty or invalid");
                    return;
                }

                var deobfuscator = new Deobfuscator();
                var result = deobfuscator.Deobfuscate(fileContent);

                string finalOutput = result.ToString();

                if (string.IsNullOrWhiteSpace(finalOutput))
                {
                    await message.Channel.SendMessageAsync("Deobfuscation failed - empty result");
                    return;
                }

                await File.WriteAllTextAsync(tempOutput, finalOutput);

                await message.Channel.SendMessageAsync("File deobfuscated successfully");

                using var fs = new FileStream(tempOutput, FileMode.Open, FileAccess.Read);
                await message.Channel.SendFileAsync(fs, "deobf.lua");
            }
            catch (Exception ex)
            {
                await message.Channel.SendMessageAsync("Error deobfuscating file: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (tempInput != null && File.Exists(tempInput))
                        File.Delete(tempInput);
                    if (tempOutput != null && File.Exists(tempOutput))
                        File.Delete(tempOutput);
                }
                catch { }
            }
        }
    }
}