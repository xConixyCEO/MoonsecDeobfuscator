using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Net;

namespace MoonsecDeobfuscator;

public static class Program
{
    /*
        Devirtualize and dump bytecode to file:
            -dev -i <path to input> -o <path to output>

        Devirtualize and dump bytecode disassembly to file:
            -dis -i <path to input> -o <path to output>

        Run as Discord bot:
            --discord-bot
    */

    public static async Task Main(string[] args)
    {
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

        if (command == "-dev")
        {
            var result = new Deobfuscator().Deobfuscate(File.ReadAllText(input));
            using var stream = new FileStream(output, FileMode.Create, FileAccess.Write);
            using var serializer = new Serializer(stream);

            serializer.Serialize(result);
        }
        else if (command == "-dis")
        {
            var result = new Deobfuscator().Deobfuscate(File.ReadAllText(input));
            File.WriteAllText(output, new Disassembler(result).Disassemble());
        }
        else
        {
            Console.WriteLine("Invalid command!");
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
                            GatewayIntents.MessageContent
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
                        body { font-family: Arial, sans-serif; margin: 40px; }
                        .container { max-width: 800px; margin: 0 auto; }
                        .status { padding: 10px; background: #4CAF50; color: white; }
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h1>🤖 Moonsec Deobfuscator Discord Bot</h1>
                        <div class='status'>✅ Bot is running!</div>
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

// Add these command classes at the end of the same file
[Group("deobfuscate", "Deobfuscate Moonsec-protected Lua files")]
public class DeobfuscateCommands : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("help", "Show help information")]
    public async Task HelpCommand()
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithTitle("🤖 Moonsec Deobfuscator")
            .WithDescription("Bot is running successfully!")
            .AddField("Commands", 
                "`/deobfuscate help` - Show this help\n" +
                "`/deobfuscate ping` - Check bot latency")
            .WithFooter("Made with MoonsecDeobfuscator")
            .WithCurrentTimestamp()
            .Build();
            
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("ping", "Check bot latency")]
    public async Task PingCommand()
    {
        var latency = Context.Client.Latency;
        var embed = new EmbedBuilder()
            .WithColor(Color.Green)
            .WithTitle("🏓 Pong!")
            .AddField("Latency", $"{latency}ms")
            .WithFooter($"Requested by {Context.User.Username}")
            .WithCurrentTimestamp()
            .Build();
            
        await RespondAsync(embed: embed);
    }

    [SlashCommand("dev", "Deobfuscate Lua file and get bytecode")]
    public async Task DeobfuscateDev(IAttachment file)
    {
        await RespondAsync("⚠️ This feature requires building the MoonsecDeobfuscator tool.", ephemeral: true);
    }

    [SlashCommand("dis", "Deobfuscate Lua file and get disassembly")]
    public async Task DeobfuscateDis(IAttachment file)
    {
        await RespondAsync("⚠️ This feature requires building the MoonsecDeobfuscator tool.", ephemeral: true);
    }
}
