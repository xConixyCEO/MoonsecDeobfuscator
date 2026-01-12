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
using System.Threading;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Bytecode.Models; // CORRECT namespace for Function

namespace GalacticBytecodeBot
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannel = 1444258745336070164;
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
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(new Game("🌙 MoonSec → Medal Pipeline"));
                Console.WriteLine($"✅ Bot connected as {_client.CurrentUser}");
                
                if (!File.Exists("/app/medal"))
                    Console.WriteLine("⚠️ WARNING: Medal not found at /app/medal");
                else
                    Console.WriteLine("✅ Medal found at /app/medal");
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            if (msg.Channel.Id != TargetChannel && msg.Channel is not SocketDMChannel)
                return;

            Console.WriteLine($"\n📥 [{DateTime.Now:HH:mm:ss}] {msg.Author.Username}: \"{msg.Content}\" | Attachments: {msg.Attachments.Count}");

            if (!msg.Content.ToLowerInvariant().Contains(".l"))
            {
                Console.WriteLine("❌ No .l command found");
                return;
            }

            if (Busy.ContainsKey(msg.Author.Id))
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ⏳ Please wait, your previous request is still processing.");
                return;
            }

            Busy[msg.Author.Id] = true;

            try
            {
                string sourceCode = null;

                if (msg.Attachments.Count > 0)
                {
                    var att = msg.Attachments.First();
                    if (!(att.Filename.ToLower().EndsWith(".lua") || att.Filename.ToLower().EndsWith(".luau") || att.Filename.ToLower().EndsWith(".txt")))
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} this file type is not allowed.");
                        Busy.Remove(msg.Author.Id);
                        return;
                    }

                    Console.WriteLine($"📎 Downloading: {att.Filename} ({att.Size} bytes)");
                    
                    using var hc = new HttpClient();
                    var bytes = await hc.GetByteArrayAsync(att.Url);
                    sourceCode = Encoding.UTF8.GetString(bytes);
                    Console.WriteLine($"✅ Downloaded {bytes.Length} bytes");
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
                    // Step 1: Generate bytecode with Moonsec library (NO using statement)
                    Console.WriteLine("🚀 Running Moonsec library...");
                    Function result;
                    var deob = new Deobfuscator();
                    result = deob.Deobfuscate(sourceCode);
                    
                    var tempBytecode = Path.Combine(Path.GetTempPath(), $"{msg.Id}.luac");
                    await statusMsg.ModifyAsync(m => m.Content = "🔄 **Processing:** Serializing bytecode...");
                    
                    // Serialize bytecode to file
                    using (var stream = new FileStream(tempBytecode, FileMode.Create, FileAccess.Write))
                    using (var serializer = new MoonsecDeobfuscator.Deobfuscation.Bytecode.Serializer(stream))
                    {
                        serializer.Serialize(result);
                    }
                    
                    Console.WriteLine($"✅ Bytecode serialized: {tempBytecode} ({new FileInfo(tempBytecode).Length} bytes)");
                    await statusMsg.ModifyAsync(m => m.Content = "🔄 **Processing:** Decompiling with Medal...");

                    // Step 2: Call Medal CLI
                    Console.WriteLine("🚀 Running Medal...");
                    var medalCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
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
                    var medalError = await medalProcess.StandardError.ReadToEndAsync();
                    
                    var medalExitTask = medalProcess.WaitForExitAsync();
                    var medalTimeoutTask = Task.Delay(TimeSpan.FromMinutes(3), medalCts.Token);
                    
                    if (await Task.WhenAny(medalExitTask, medalTimeoutTask) == medalTimeoutTask)
                    {
                        Console.WriteLine("❌ Medal timed out after 3 minutes!");
                        medalProcess.Kill();
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Medal timed out.");
                        return;
                    }

                    Console.WriteLine($"Medal Exit Code: {medalProcess.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(medalError)) Console.WriteLine($"Medal Error: {medalError}");

                    try { File.Delete(tempBytecode); } catch { }

                    if (medalProcess.ExitCode != 0)
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Medal failed: {medalError}");
                        return;
                    }

                    Console.WriteLine($"✅ Decompiled {decompiledCode.Length} characters");
                    
                    // Step 3: Send decompiled Lua
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ **Deobfuscated & Decompiled**")
                        .WithColor(Color.Green)
                        .WithFooter($"Galactic Deobfuscator | {msg.Author.Username}");

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
                    Console.WriteLine("✅ Complete!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Processing Error: {ex}");
                    await msg.Channel.SendMessageAsync($"{msg.Author.Mention} ❌ Processing error: {ex.Message}");
                }

                try { await msg.DeleteAsync(); } catch { }
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
