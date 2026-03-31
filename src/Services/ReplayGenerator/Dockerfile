FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY common.props SplatSourceGeneratorWorkaround.cs ./
COPY src/KyoshinEewViewer.Core/ src/KyoshinEewViewer.Core/
COPY src/Services/ReplayGenerator/ src/Services/ReplayGenerator/

RUN dotnet publish src/Services/ReplayGenerator/ReplayGenerator.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system --gid 1001 appgroup && \
    useradd --system --uid 1001 --gid appgroup appuser
USER appuser

COPY --from=build --chown=appuser:appgroup /app/publish .

ENTRYPOINT ["dotnet", "ReplayGenerator.dll"]
