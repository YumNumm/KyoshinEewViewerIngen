FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY common.props ./
COPY nuget.config ./
COPY src/KyoshinEewViewer.Core/*.csproj ./src/KyoshinEewViewer.Core/
COPY src/Sandboxes/ReplayGenerator/*.csproj ./src/Sandboxes/ReplayGenerator/

RUN dotnet restore src/Sandboxes/ReplayGenerator/ReplayGenerator.csproj

COPY . .

# buildx の --platform に合わせた RID（常に linux-x64 だと arm64 イメージが壊れる／ビルドが失敗することがある）
ARG TARGETARCH
RUN set -eux; \
    RID=linux-x64; \
    case "${TARGETARCH:-amd64}" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    dotnet publish src/Sandboxes/ReplayGenerator/ReplayGenerator.csproj \
        -c Release \
        -r "${RID}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN chmod +x ReplayGenerator

ENV PORT=5000
ENV TZ=Asia/Tokyo

EXPOSE 5000

ENTRYPOINT ["./ReplayGenerator"]
