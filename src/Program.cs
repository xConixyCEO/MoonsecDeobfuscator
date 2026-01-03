using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace MoonsecDeobfuscator;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load environment variables from .env file if it exists
        try
        {
            DotEnv.Load();
        }
        catch
        {
            Console.WriteLine("Note: No .env file found, using environment variables only");
        }

        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("❌ ERROR: DISCORD_BOT_TOKEN environment variable is not set!");
            Console.WriteLine("Please set it in your Render environment variables or .env file");
            return;
        }

        // Start HTTP server on port 3000 for Render health checks
        _ = StartHttpServer();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Configure Discord client
                services.AddSingleton<DiscordSocketClient>(provider =>
                {
                    return new DiscordSocketClient(new DiscordSocketConfig
                    {
                        LogLevel = LogSeverity.Info,
                        GatewayIntents = GatewayIntents.Guilds | 
                                        GatewayIntents.GuildMessages |
                                        GatewayIntents.MessageContent,
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = 100
                    });
                });

                // Configure Interaction Service
                services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
                services.AddSingleton<InteractionHandler>();
                
                // Add hosted service
                services.AddHostedService<DiscordBotHostedService>();
            })
            .Build();

        await host.RunAsync();
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
                
                string responseString = @"
                <html>
                    <head>
                        <title>Moonsec Deobfuscator Discord Bot</title>
                        <style>
                            body { font-family: Arial, sans-serif; margin: 40px; background: #f0f0f0; }
                            .container { max-width: 800px; margin: 0 auto; background: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
                            h1 { color: #333; }
                            .status { padding: 10px; background: #4CAF50; color: white; border-radius: 5px; }
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <h1>🤖 Moonsec Deobfuscator Discord Bot</h1>
                            <div class='status'>✅ Bot is running and healthy!</div>
                            <p>Use <code>/deobfuscate</code> commands in Discord.</p>
                        </div>
                    </body>
                </html>";
                
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
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
}

public class DiscordBotHostedService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly InteractionHandler _handler;
    private readonly IServiceProvider _services;

    public DiscordBotHostedService(
        DiscordSocketClient client,
        InteractionService interactions,
        InteractionHandler handler,
        IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _handler = handler;
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;

        await _handler.InitializeAsync();
        
        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        
        Console.WriteLine("🤖 Discord Bot starting...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {log}");
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        Console.WriteLine($"✅ {_client.CurrentUser.Username} is connected!");
        
        // Register commands
        await _interactions.RegisterCommandsGloballyAsync();
        Console.WriteLine("📝 Slash commands registered globally");
    }
}

public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;

    public InteractionHandler(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
    }

    public async Task InitializeAsync()
    {
        // Add command modules
        await _interactions.AddModuleAsync<DeobfuscateCommands>(_services);

        _client.InteractionCreated += HandleInteraction;
        _interactions.SlashCommandExecuted += SlashCommandExecuted;
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling interaction: {ex}");
            
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                await interaction.RespondAsync($"❌ Error: {ex.Message}", ephemeral: true);
            }
        }
    }

    private Task SlashCommandExecuted(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Command execution failed: {result.ErrorReason}");
        }
        return Task.CompletedTask;
    }
}

[Group("deobfuscate", "Deobfuscate Moonsec-protected Lua files")]
public class DeobfuscateCommands : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("dev", "Deobfuscate Lua file and get bytecode")]
    public async Task DevCommand(IAttachment file)
    {
        await HandleDeobfuscation(file, "dev");
    }

    [SlashCommand("dis", "Deobfuscate Lua file and get disassembly")]
    public async Task DisCommand(IAttachment file)
    {
        await HandleDeobfuscation(file, "dis");
    }

    [SlashCommand("help", "Show help information")]
    public async Task HelpCommand()
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithTitle("Moonsec Deobfuscator Help")
            .WithDescription("Deobfuscate Moonsec-protected Lua files")
            .AddField("Commands", 
                "`/deobfuscate dev` - Upload a .lua file to get bytecode\n" +
                "`/deobfuscate dis` - Upload a .lua file to get disassembly")
            .WithFooter("Attach a .lua file when using the commands")
            .Build();
            
        await RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleDeobfuscation(IAttachment file, string mode)
    {
        if (!file.Filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync("❌ Please provide a .lua file", ephemeral: true);
            return;
        }

        await DeferAsync();

        try
        {
            using var httpClient = new HttpClient();
            var luaContent = await httpClient.GetStringAsync(file.Url);
            
            var deobfuscator = new Deobfuscator();
            var result = deobfuscator.Deobfuscate(luaContent);

            if (mode == "dev")
            {
                using var memoryStream = new MemoryStream();
                using var serializer = new Serializer(memoryStream);
                serializer.Serialize(result);
                
                var bytecode = memoryStream.ToArray();
                var tempFile = Path.GetTempFileName() + ".bytecode";
                await File.WriteAllBytesAsync(tempFile, bytecode);
                
                await FollowupWithFileAsync(tempFile, 
                    $"{Path.GetFileNameWithoutExtension(file.Filename)}_deobfuscated.bytecode");
                
                File.Delete(tempFile);
            }
            else if (mode == "dis")
            {
                var disassembler = new Disassembler(result);
                var disassembly = disassembler.Disassemble();
                
                var tempFile = Path.GetTempFileName() + ".txt";
                await File.WriteAllTextAsync(tempFile, disassembly);
                
                await FollowupWithFileAsync(tempFile, 
                    $"{Path.GetFileNameWithoutExtension(file.Filename)}_disassembly.txt");
                
                File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Error: {ex.Message}", ephemeral: true);
        }
    }
}
