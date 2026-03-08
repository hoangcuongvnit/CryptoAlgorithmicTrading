# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Notifier/Notifier.Worker/Notifier.Worker.csproj", "src/Services/Notifier/Notifier.Worker/"]

# Restore
RUN dotnet restore "src/Services/Notifier/Notifier.Worker/Notifier.Worker.csproj"

# Copy all source code
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Services/Notifier/Notifier.Worker/", "src/Services/Notifier/Notifier.Worker/"]

# Build
RUN dotnet build "src/Services/Notifier/Notifier.Worker/Notifier.Worker.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Notifier/Notifier.Worker/Notifier.Worker.csproj" \
    -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Notifier.Worker.dll"]
