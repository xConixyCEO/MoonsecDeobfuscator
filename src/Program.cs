using Discord;
using Discord.WebSocket;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly Dictionary<ulong, bool> Busy = new Dictionary<ulong, bool>();
        private static readonly HttpClient HttpClient = new HttpClient();

        public static async Task Main()
        {
            var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("❌ DISCORD_TOKEN environment variable is missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.Online);
                await _client.SetActivityAsync(new Game("🌙 MoonSec → Medal Pipeline"));
                Console.WriteLine($"✅ Bot connected as {_client.CurrentUser}");
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            if (!msg.Content.ToLowerInvariant().Contains(".l"))
                return;

            if (Busy.ContainsKey(msg.Author.Id))
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ⏳ Please wait, your previous request is processing.");
                return;
            }

            Busy[msg.Author.Id] = true;

            try
            {
                string sourceCode = null;

                if (msg.Attachments.Count > 0)
                {
                    var att = msg.Attachments.First();
                    using var hc = new HttpClient();
                    var bytes = await hc.GetByteArrayAsync(att.Url);
                    sourceCode = Encoding.UTF8.GetString(bytes);
                }

                if (sourceCode == null)
                {
                    await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Please attach a file with your .l command.");
                    Busy.Remove(msg.Author.Id);
                    return;
                }

                var statusMsg = await msg.Channel.SendMessageAsync("🔄 **Processing:** MoonSec deobfuscation & bytecode dump...");

                try
                {
                    // Step 1: Write source to temp file
                    var tempInput = Path.Combine(Path.GetTempPath(), $"{msg.Id}.lua");
                    var tempBytecode = Path.Combine(Path.GetTempPath(), $"{msg.Id}.luac");
                    await File.WriteAllTextAsync(tempInput, sourceCode);

                    // Step 2: Run Moonsec CLI
                    var moonsecProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/app/MoonsecDeobfuscator",
                            Arguments = $"-dev -i \"{tempInput}\" -o \"{tempBytecode}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    moonsecProcess.Start();
                    await moonsecProcess.WaitForExitAsync();

                    try { File.Delete(tempInput); } catch { }

                    if (moonsecProcess.ExitCode != 0 || !File.Exists(tempBytecode))
                    {
                        var error = await moonsecProcess.StandardError.ReadToEndAsync();
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Moonsec error: {error}");
                        return;
                    }

                    await statusMsg.ModifyAsync(m => m.Content = "🔄 **Processing:** Decompiling with Medal...");

                    // Step 3: Call Medal
                    var medalProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/app/medal",
                            Arguments = $"\"{tempBytecode}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    medalProcess.Start();
                    var decompiledCode = await medalProcess.StandardOutput.ReadToEndAsync();
                    await medalProcess.WaitForExitAsync();

                    try { File.Delete(tempBytecode); } catch { }

                    if (medalProcess.ExitCode != 0)
                    {
                        var error = await medalProcess.StandardError.ReadToEndAsync();
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Medal error: {error}");
                        return;
                    }

                    // Step 4: Send result
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ **Deobfuscated & Decompiled**")
                        .WithColor(Color.Green)
                        .WithFooter($"MoonsecDeobfuscator | {msg.Author.Username}");

                    if (decompiledCode.Length > 2000)
                    {
                        var tempFile = Path.Combine(Path.GetTempPath(), $"{msg.Id}_decompiled.lua");
                        await File.WriteAllTextAsync(tempFile, decompiledCode);
                        
                        await using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read))
                        {
                            await msg.Channel.SendFileAsync(fs, "decompiled.lua", 
                                $"{msg.Author.Mention} here is your decompiled code:", embed: embed.Build());
                        }
                        
                        try { File.Delete(tempFile); } catch { }
                    }
                    else
                    {
                        embed.WithDescription($"```lua\n{decompiledCode}\n```");
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention}", embed: embed.Build());
                    }

                    await statusMsg.DeleteAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Processing Error: {ex}");
                    await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Processing error: {ex.Message}");
                }

                try
                {
                    await msg.DeleteAsync();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Global Error: {ex}");
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ An error occurred.");
            }
            finally
            {
                if (Busy.ContainsKey(msg.Author.Id))
                    Busy.Remove(msg.Author.Id);
            }
        }
    }
}
