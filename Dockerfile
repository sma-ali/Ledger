# Étape de compilation : l'image SDK contient le compilateur, elle pèse
# lourd et ne doit pas se retrouver en exécution.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Les fichiers projet sont copiés seuls et restaurés en premier. Tant
# qu'aucune dépendance ne change, Docker réutilise cette couche et ne
# retélécharge rien, même si tout le code a changé.
COPY global.json Directory.Build.props ./
COPY src/Ledger.Domain/Ledger.Domain.csproj           src/Ledger.Domain/
COPY src/Ledger.Ingestion/Ledger.Ingestion.csproj     src/Ledger.Ingestion/
COPY src/Ledger.Persistence/Ledger.Persistence.csproj src/Ledger.Persistence/
COPY src/Ledger.Batch/Ledger.Batch.csproj             src/Ledger.Batch/
RUN dotnet restore src/Ledger.Batch/Ledger.Batch.csproj

COPY src/ src/
RUN dotnet publish src/Ledger.Batch/Ledger.Batch.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# Image finale : le runtime seul, sans compilateur ni sources.
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "Ledger.Batch.dll"]
