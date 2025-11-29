# build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY *.sln ./
COPY *.csproj ./

RUN dotnet restore -v diag

COPY . ./

RUN dotnet publish MoonsecDeobfuscator.csproj -c Release -o out -v diag

# runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/out ./

CMD ["dotnet", "MoonsecDeobfuscator.dll"]
