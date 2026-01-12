# Stage 1: Build MoonsecDeobfuscator
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS moonsec-builder
WORKDIR /build
COPY . .
RUN dotnet publish -c Release -o /app

# Stage 2: Clone & Build Medal (Rust)
FROM rust:alpine AS medal-builder
WORKDIR /build
RUN apk add --no-cache git build-base
RUN rustup install nightly
RUN git clone https://github.com/xConixyCEO/medal.git
WORKDIR /build/medal
RUN cargo +nightly build --release --bin medal

# Stage 3: Runtime (with Lua!)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
WORKDIR /app

# Install curl + LUA libraries + create symlink for NLua
RUN apk add --no-cache curl lua5.4 lua5.4-dev && \
    # Create symlink so NLua can find lua54.so
    ln -sf /usr/lib/liblua5.4.so /usr/lib/liblua54.so

# Copy bot files
COPY --from=moonsec-builder /app/* ./

# Copy Medal binary
COPY --from=medal-builder /build/medal/target/release/medal ./medal

# Make executables runnable
RUN chmod +x ./MoonsecDeobfuscator ./medal

CMD ["dotnet", "MoonsecDeobfuscator.dll"]
