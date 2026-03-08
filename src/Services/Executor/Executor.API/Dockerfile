# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Executor/Executor.API/Executor.API.csproj", "src/Services/Executor/Executor.API/"]

# Restore
RUN dotnet restore "src/Services/Executor/Executor.API/Executor.API.csproj"

# Copy all source code
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Services/Executor/Executor.API/", "src/Services/Executor/Executor.API/"]

# Build
RUN dotnet build "src/Services/Executor/Executor.API/Executor.API.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Executor/Executor.API/Executor.API.csproj" \
    -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .

# gRPC port exposure
EXPOSE 5014

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5094/health || exit 1

ENTRYPOINT ["dotnet", "Executor.API.dll"]
