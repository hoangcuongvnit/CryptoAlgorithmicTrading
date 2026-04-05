FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj", "src/Services/FinancialLedger/FinancialLedger.Worker/"]

RUN dotnet restore "src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj"

COPY . .
RUN dotnet publish "src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5097
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s CMD curl -f http://localhost:5097/health || exit 1

ENTRYPOINT ["dotnet", "FinancialLedger.Worker.dll"]
