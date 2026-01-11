using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MoonsecDeobfuscator
{
    public class Bridge
    {
        public static void RunWebApi(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCors(options => options.AddPolicy("AllowAll", 
                policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            var app = builder.Build();
            app.UseCors("AllowAll");

            // POST /lua/deobfuscate
            app.MapPost("/lua/deobfuscate", async (HttpContext context) => {
                try
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var obfuscatedLua = await reader.ReadToEndAsync();
                    
                    // Write to temp files
                    var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.lua");
                    var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.luac");
                    await File.WriteAllTextAsync(tempInput, obfuscatedLua);

                    // Step 1: Run MoonsecDeobfuscator
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/app/MoonsecDeobfuscator",
                            Arguments = $"-dev -i \"{tempInput}\" -o \"{tempOutput}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0 || !File.Exists(tempOutput))
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync($"Moonsec failed: {error}");
                        return;
                    }

                    // Step 2: Run Medal luau-lifter on the bytecode
                    var medalProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/app/medal",
                            Arguments = $"\"{tempOutput}\"", // Medal outputs to stdout
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    medalProcess.Start();
                    var decompiledCode = await medalProcess.StandardOutput.ReadToEndAsync();
                    await medalProcess.WaitForExitAsync();

                    if (medalProcess.ExitCode != 0)
                    {
                        var error = await medalProcess.StandardError.ReadToEndAsync();
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync($"Medal failed: {error}");
                        return;
                    }

                    // Cleanup
                    File.Delete(tempInput);
                    File.Delete(tempOutput);

                    // Return decompiled code
                    await context.Response.WriteAsync(decompiledCode);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync($"Pipeline error: {ex.Message}");
                }
            });

            app.MapGet("/health", () => "Moonsec+Medal Bridge is running!");
            app.Run("http://0.0.0.0:3000");
        }
    }
}
