# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["CryptoAlgorithmicTrading.sln", "."]

# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Gateway/Gateway.API/Gateway.API.csproj", "src/Gateway/Gateway.API/"]

# Restore
RUN dotnet restore "CryptoAlgorithmicTrading.sln"

# Copy all source code
COPY ["src/", "src/"]

# Build
RUN dotnet build "src/Gateway/Gateway.API/Gateway.API.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Gateway/Gateway.API/Gateway.API.csproj" \
    -c Release -o /app/publish \
    --no-build

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .

# HTTP port exposure
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "Gateway.API.dll"]
