# Stage 1: Build MoonsecDeobfuscator
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS moonsec-builder
WORKDIR /build
COPY . .
# Suppress nullable reference type warnings (CS8632) and CLSCompliant warnings (CS3021)
RUN dotnet publish -c Release -o /app /p:NoWarn="8632;3021"

# Stage 2: Clone & Build Medal (Rust)
FROM rust:alpine AS medal-builder
WORKDIR /build
RUN apk add --no-cache git build-base
RUN rustup install nightly
RUN git clone https://github.com/xConixyCEO/medal.git
WORKDIR /build/medal
RUN cargo +nightly build --release --bin luau-lifter

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

# Copy Moonsec binary and DLLs
COPY --from=moonsec-builder /app/* ./

# Copy Medal binary directly from builder stage (FIXED)
COPY --from=medal-builder /build/medal/target/release/luau-lifter ./medal

# Make executables runnable
RUN chmod +x ./MoonsecDeobfuscator ./medal

CMD ["dotnet", "MoonsecDeobfuscator.dll"]
