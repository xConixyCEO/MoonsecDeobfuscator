using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using dotenv.net;

namespace MoonsecDeobfuscator.DiscordBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load environment variables
        DotEnv.Load();
        
        // Get configuration from environment variables
        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        var clientId = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
        var guildId = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID"); // Optional for testing
        
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("❌ DISCORD_BOT_TOKEN not found in environment variables!");
            return;
        }

        // Create host builder for background service
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<DiscordSocketClient>(provider =>
                {
                    return new DiscordSocketClient(new DiscordSocketConfig
                    {
                        LogLevel = LogSeverity.Info,
                        GatewayIntents = GatewayIntents.GuildMessages | 
                                        GatewayIntents.MessageContent |
                                        GatewayIntents.Guilds,
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = 100
                    });
                });
                
                services.AddSingleton<InteractionService>();
                services.AddSingleton<InteractionHandler>();
                services.AddHostedService<DiscordBotService>();
            })
            .Build();

        await host.RunAsync();
    }
}

public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly InteractionHandler _handler;
    private readonly IHostApplicationLifetime _lifetime;
    
    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        InteractionHandler handler,
        IHostApplicationLifetime lifetime)
    {
        _client = client;
        _interactions = interactions;
        _handler = handler;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Start HTTP server on port 3000 for Render
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://*:3000/");
        httpListener.Start();
        
        Console.WriteLine("🌐 HTTP Server started on port 3000");
        
        // Handle HTTP requests in background
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await httpListener.GetContextAsync();
                var response = context.Response;
                
                var responseString = "<html><body><h1>Moonsec Deobfuscator Discord Bot</h1><p>Bot is running!</p></body></html>";
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
        });

        // Setup Discord client
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
        
        // Register slash commands globally (or to specific guild for testing)
        var guildId = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
        
        if (!string.IsNullOrEmpty(guildId) && ulong.TryParse(guildId, out var guildUlong))
        {
            // Register to specific guild for faster testing
            await _interactions.RegisterCommandsToGuildAsync(guildUlong);
            Console.WriteLine($"📝 Slash commands registered to guild: {guildId}");
        }
        else
        {
            // Register globally (takes up to 1 hour to propagate)
            await _interactions.RegisterCommandsGloballyAsync();
            Console.WriteLine("📝 Slash commands registered globally");
        }
    }
}

public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;

    public InteractionHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
    }

    public async Task InitializeAsync()
    {
        await _interactions.AddModulesAsync(System.Reflection.Assembly.GetEntryAssembly(), _services);
        
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
                await interaction.GetOriginalResponseAsync()
                    .ContinueWith(async (msg) => await msg.Result.DeleteAsync());
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

// Slash command module
[Group("deobfuscate", "Deobfuscate Moonsec-protected Lua files")]
public class DeobfuscateCommands : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("dev", "Deobfuscate Lua file and get bytecode")]
    public async Task DeobfuscateDev(
        [Summary("file", "The Lua file to deobfuscate")] IAttachment file)
    {
        if (!file.Filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync("❌ Please provide a .lua file", ephemeral: true);
            return;
        }

        await DeferAsync(); // Acknowledge the command
        
        try
        {
            using var httpClient = new HttpClient();
            var luaContent = await httpClient.GetStringAsync(file.Url);
            
            var result = new Deobfuscator().Deobfuscate(luaContent);
            
            // Convert to bytecode
            using var memoryStream = new MemoryStream();
            using var serializer = new Serializer(memoryStream);
            serializer.Serialize(result);
            
            var bytecode = memoryStream.ToArray();
            var base64 = Convert.ToBase64String(bytecode);
            
            // Create embed response
            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("✅ Deobfuscation Complete")
                .WithDescription($"**File:** {file.Filename}")
                .AddField("Status", "Successfully deobfuscated")
                .WithFooter($"Requested by {Context.User.Username}")
                .WithCurrentTimestamp()
                .Build();
            
            // Send bytecode as file if too large
            if (base64.Length > 1900)
            {
                var tempFile = Path.GetTempFileName() + ".bytecode";
                await File.WriteAllBytesAsync(tempFile, bytecode);
                
                await FollowupWithFileAsync(tempFile, 
                    $"{Path.GetFileNameWithoutExtension(file.Filename)}_deobfuscated.bytecode",
                    embed: embed);
                
                File.Delete(tempFile);
            }
            else
            {
                await FollowupAsync(embed: embed);
                await FollowupAsync($"Bytecode (Base64):\n```\n{base64}\n```", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Error during deobfuscation: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("dis", "Deobfuscate Lua file and get disassembly")]
    public async Task DeobfuscateDis(
        [Summary("file", "The Lua file to deobfuscate")] IAttachment file)
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
            
            var result = new Deobfuscator().Deobfuscate(luaContent);
            var disassembly = new Disassembler(result).Disassemble();
            
            // Create embed response
            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle("📄 Disassembly Complete")
                .WithDescription($"**File:** {file.Filename}")
                .AddField("Status", "Successfully disassembled")
                .WithFooter($"Requested by {Context.User.Username}")
                .WithCurrentTimestamp()
                .Build();
            
            // Send as file if too large
            if (disassembly.Length > 1900)
            {
                var tempFile = Path.GetTempFileName() + ".txt";
                await File.WriteAllTextAsync(tempFile, disassembly);
                
                await FollowupWithFileAsync(tempFile, 
                    $"{Path.GetFileNameWithoutExtension(file.Filename)}_disassembly.txt",
                    embed: embed);
                
                File.Delete(tempFile);
            }
            else
            {
                var truncated = disassembly.Length > 1000 ? 
                    disassembly.Substring(0, 1000) + "\n... (truncated)" : 
                    disassembly;
                    
                await FollowupAsync(embed: embed);
                await FollowupAsync($"Disassembly:\n```lua\n{truncated}\n```");
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Error during disassembly: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("help", "Show help information")]
    public async Task Help()
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Purple)
            .WithTitle("🤖 Moonsec Deobfuscator")
            .WithDescription("Deobfuscate Moonsec-protected Lua files using slash commands")
            .AddField("Commands", 
                "`/deobfuscate dev [file]` - Deobfuscate and get bytecode\n" +
                "`/deobfuscate dis [file]` - Deobfuscate and get disassembly\n" +
                "`/deobfuscate help` - Show this help")
            .AddField("Usage", 
                "1. Type `/deobfuscate` and choose a command\n" +
                "2. Attach a .lua file when prompted")
            .AddField("Note", 
                "Files must be .lua files protected with Moonsec")
            .WithFooter("Made with MoonsecDeobfuscator")
            .WithCurrentTimestamp()
            .Build();
        
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("ping", "Check bot latency")]
    public async Task Ping()
    {
        var latency = Context.Client.Latency;
        var embed = new EmbedBuilder()
            .WithColor(latency < 100 ? Color.Green : latency < 200 ? Color.Orange : Color.Red)
            .WithTitle("🏓 Pong!")
            .AddField("Latency", $"{latency}ms")
            .AddField("Status", latency < 100 ? "Excellent" : latency < 200 ? "Good" : "Slow")
            .WithCurrentTimestamp()
            .Build();
        
        await RespondAsync(embed: embed);
    }
}
