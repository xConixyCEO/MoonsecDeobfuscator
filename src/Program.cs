using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Net;

namespace MoonsecDeobfuscator;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Load environment variables
        try
        {
            dotenv.net.DotEnv.Load();
        }
        catch
        {
            Console.WriteLine("Note: No .env file found, using environment variables");
        }

        // Check if we should run as Discord bot
        if (args.Length > 0 && args[0] == "--discord-bot")
        {
            await RunAsDiscordBot();
        }
        else
        {
            RunAsCli(args);
        }
    }

    private static void RunAsCli(string[] args)
    {
        /*
            Original CLI functionality
            Devirtualize and dump bytecode to file:
                -dev -i <path to input> -o <path to output>

            Devirtualize and dump bytecode disassembly to file:
                -dis -i <path to input> -o <path to output>
        */

        if (args.Length != 5 || args[1] != "-i" || args[3] != "-o")
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("Devirtualize and dump bytecode to file:\n\t-dev -i <input> -o <output>");
            Console.WriteLine("Devirtualize and dump bytecode disassembly to file:\n\t-dis -i <input> -o <output>");
            Console.WriteLine("\nRun as Discord bot:\n\t--discord-bot");
            return;
        }

        var command = args[0];
        var input = args[2];
        var output = args[4];

        if (!File.Exists(input))
        {
            Console.WriteLine("Invalid input path!");
            return;
        }

        try
        {
            if (command == "-dev")
            {
                var result = new Deobfuscator().Deobfuscate(File.ReadAllText(input));
                using var stream = new FileStream(output, FileMode.Create, FileAccess.Write);
                using var serializer = new Serializer(stream);

                serializer.Serialize(result);
                Console.WriteLine($"✅ Bytecode saved to: {output}");
            }
            else if (command == "-dis")
            {
                var result = new Deobfuscator().Deobfuscate(File.ReadAllText(input));
                File.WriteAllText(output, new Disassembler(result).Disassemble());
                Console.WriteLine($"✅ Disassembly saved to: {output}");
            }
            else
            {
                Console.WriteLine("Invalid command!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }
    }

    private static async Task RunAsDiscordBot()
    {
        Console.WriteLine("🤖 Starting Moonsec Deobfuscator Discord Bot...");
        
        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("❌ ERROR: DISCORD_BOT_TOKEN environment variable is not set!");
            Console.WriteLine("Please set it in your Render environment variables");
            return;
        }

        // Start HTTP server on port 3000 for Render
        _ = Task.Run(StartHttpServer);

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = GatewayIntents.Guilds | 
                            GatewayIntents.GuildMessages |
                            GatewayIntents.MessageContent,
            MessageCacheSize = 100
        });

        var interactions = new InteractionService(client);

        client.Log += LogAsync;
        client.Ready += async () => await ReadyAsync(client, interactions);
        client.InteractionCreated += async (interaction) => await HandleInteraction(client, interactions, interaction);

        await interactions.AddModuleAsync<DeobfuscateCommands>(null);

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        await Task.Delay(-1);
    }

    private static async Task StartHttpServer()
    {
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://*:3000/");
            listener.Start();
            Console.WriteLine("✅ HTTP Server started on port 3000");

            while (true)
            {
                var context = await listener.GetContextAsync();
                var response = context.Response;
                
                string html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Moonsec Deobfuscator Discord Bot</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; color: white; }
                        .container { max-width: 800px; margin: 0 auto; background: rgba(255,255,255,0.1); padding: 30px; border-radius: 15px; backdrop-filter: blur(10px); }
                        h1 { margin-top: 0; }
                        .status { display: inline-block; padding: 10px 20px; background: #4CAF50; border-radius: 5px; font-weight: bold; }
                        code { background: rgba(0,0,0,0.3); padding: 2px 6px; border-radius: 3px; }
                        .commands { background: rgba(0,0,0,0.2); padding: 20px; border-radius: 10px; margin: 20px 0; }
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h1>🤖 Moonsec Deobfuscator Discord Bot</h1>
                        <div class='status'>✅ Bot is running and healthy!</div>
                        <p>This bot deobfuscates Moonsec-protected Lua files.</p>
                        
                        <div class='commands'>
                            <h3>📋 Available Commands:</h3>
                            <p><code>/deobfuscate dev</code> - Upload .lua file to get bytecode</p>
                            <p><code>/deobfuscate dis</code> - Upload .lua file to get disassembly</p>
                            <p><code>/deobfuscate help</code> - Show help information</p>
                            <p><code>/deobfuscate ping</code> - Check bot latency</p>
                        </div>
                        
                        <p><strong>Usage:</strong> Attach a .lua file when using the dev/dis commands in Discord.</p>
                    </div>
                </body>
                </html>";
                
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
                response.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ HTTP Server error: {ex.Message}");
        }
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {log}");
        return Task.CompletedTask;
    }

    private static async Task ReadyAsync(DiscordSocketClient client, InteractionService interactions)
    {
        Console.WriteLine($"✅ {client.CurrentUser.Username} is connected!");
        Console.WriteLine($"👤 Bot ID: {client.CurrentUser.Id}");
        
        try
        {
            await interactions.RegisterCommandsGloballyAsync();
            Console.WriteLine("📝 Slash commands registered globally");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to register commands: {ex.Message}");
        }
    }

    private static async Task HandleInteraction(DiscordSocketClient client, InteractionService interactions, SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            await interactions.ExecuteCommandAsync(context, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling interaction: {ex}");
            
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                try
                {
                    await interaction.RespondAsync($"❌ Error: {ex.Message}", ephemeral: true);
                }
                catch
                {
                    // Interaction may already be responded to
                }
            }
        }
    }
}

// Command module for slash commands
[Group("deobfuscate", "Deobfuscate Moonsec-protected Lua files")]
public class DeobfuscateCommands : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("dev", "Deobfuscate Lua file and get bytecode")]
    public async Task DeobfuscateDev(IAttachment file)
    {
        await ProcessFile(file, "dev");
    }

    [SlashCommand("dis", "Deobfuscate Lua file and get disassembly")]
    public async Task DeobfuscateDis(IAttachment file)
    {
        await ProcessFile(file, "dis");
    }

    [SlashCommand("help", "Show help information")]
    public async Task HelpCommand()
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithTitle("🤖 Moonsec Deobfuscator")
            .WithDescription("Deobfuscate Moonsec-protected Lua files using slash commands")
            .AddField("Commands", 
                "`/deobfuscate dev` - Upload .lua file to get bytecode\n" +
                "`/deobfuscate dis` - Upload .lua file to get disassembly\n" +
                "`/deobfuscate help` - Show this help\n" +
                "`/deobfuscate ping` - Check bot latency")
            .AddField("Usage", "Attach a .lua file when using the dev/dis commands")
            .AddField("Note", "Only works with Moonsec-protected Lua files")
            .WithFooter("Made with MoonsecDeobfuscator")
            .WithCurrentTimestamp()
            .Build();
            
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("ping", "Check bot latency")]
    public async Task PingCommand()
    {
        var latency = Context.Client.Latency;
        var color = latency < 100 ? Color.Green : latency < 200 ? Color.Orange : Color.Red;
        
        var embed = new EmbedBuilder()
            .WithColor(color)
            .WithTitle("🏓 Pong!")
            .AddField("Latency", $"{latency}ms")
            .AddField("Status", latency < 100 ? "Excellent" : latency < 200 ? "Good" : "High")
            .WithFooter($"Requested by {Context.User.Username}")
            .WithCurrentTimestamp()
            .Build();
            
        await RespondAsync(embed: embed);
    }

    private async Task ProcessFile(IAttachment file, string mode)
    {
        if (!file.Filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync("❌ Please provide a .lua file", ephemeral: true);
            return;
        }

        if (file.Size > 10 * 1024 * 1024) // 10MB limit
        {
            await RespondAsync("❌ File size too large (max 10MB)", ephemeral: true);
            return;
        }

        await DeferAsync();

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var luaContent = await httpClient.GetStringAsync(file.Url);
            
            var deobfuscator = new Deobfuscator();
            var result = deobfuscator.Deobfuscate(luaContent);

            var baseFilename = Path.GetFileNameWithoutExtension(file.Filename);
            
            if (mode == "dev")
            {
                // Get bytecode
                using var memoryStream = new MemoryStream();
                using var serializer = new Serializer(memoryStream);
                serializer.Serialize(result);
                
                var bytecode = memoryStream.ToArray();
                var tempFile = Path.GetTempFileName() + ".bytecode";
                await File.WriteAllBytesAsync(tempFile, bytecode);
                
                await FollowupWithFileAsync(tempFile, $"{baseFilename}_deobfuscated.bytecode");
                File.Delete(tempFile);
            }
            else if (mode == "dis")
            {
                // Get disassembly
                var disassembler = new Disassembler(result);
                var disassembly = disassembler.Disassemble();
                
                var tempFile = Path.GetTempFileName() + ".txt";
                await File.WriteAllTextAsync(tempFile, disassembly);
                
                await FollowupWithFileAsync(tempFile, $"{baseFilename}_disassembly.txt");
                File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Deobfuscation error: {ex}");
            await FollowupAsync($"❌ Error processing file: {ex.Message}", ephemeral: true);
        }
    }
}
