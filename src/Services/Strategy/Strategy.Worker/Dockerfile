# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj", "src/Services/Strategy/Strategy.Worker/"]

# Restore
RUN dotnet restore "src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj"

# Copy all source code
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Services/Strategy/Strategy.Worker/", "src/Services/Strategy/Strategy.Worker/"]

# Build
RUN dotnet build "src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj" \
    -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Strategy.Worker.dll"]
