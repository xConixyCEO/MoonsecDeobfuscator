FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Build our Discord bot only
WORKDIR /app
COPY MoonsecDeobfuscatorBot/*.csproj .
RUN dotnet restore

COPY MoonsecDeobfuscatorBot/ .
RUN dotnet publish -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Create a non-root user
RUN groupadd -r appgroup && useradd -r -g appgroup appuser
USER appuser

# Expose port for Render
EXPOSE 3000

# Start the Discord bot
ENTRYPOINT ["dotnet", "MoonsecDeobfuscatorBot.dll"]
