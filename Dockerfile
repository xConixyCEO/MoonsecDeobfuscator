FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project file and restore dependencies
COPY MoonsecDeobfuscator.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

# Create a non-root user (recommended for security)
RUN addgroup --system --gid 1000 appgroup && \
    adduser --system --uid 1000 --ingroup appgroup appuser
USER appuser

# Expose port for Render
EXPOSE 3000

# Start the Discord bot
ENTRYPOINT ["dotnet", "MoonsecDeobfuscator.dll", "--discord-bot"]
