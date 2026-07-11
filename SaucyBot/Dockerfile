ARG CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:10.0-alpine3.24@sha256:bb250b6e59f8270c30aedb2c83b4aae3228e9ba3280a17cb5ce537c34108e3d4 AS base

WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.24@sha256:fc785a84b314fce6b7adb29ecc18542d7416f2e05c03ec1ebdb222eadaeafa50 AS build

WORKDIR /src

COPY ["SaucyBot.csproj", "SaucyBot/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/http-cache \
    --mount=type=cache,target=/root/.local/share/NuGet/plugin-cache \
    --mount=type=cache,target=/tmp/NuGetScratchs \
    dotnet restore "SaucyBot/SaucyBot.csproj" -a "${TARGETARCH}"

COPY . SaucyBot/

WORKDIR /src/SaucyBot

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build "SaucyBot.csproj" -c "${CONFIGURATION}" -a "${TARGETARCH}" -o /app/build

FROM build AS publish

RUN dotnet publish "SaucyBot.csproj" -c "${CONFIGURATION}" -a "${TARGETARCH}" -o /app/publish

FROM base AS final

WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

RUN set -xe; \
    apk add --no-cache --update \
        gcompat \
        icu-libs \
        ffmpeg

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "SaucyBot.dll"]
