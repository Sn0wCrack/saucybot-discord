ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine3.18@sha256:d4f7f583c70c14ef532e5287f60299306e30ef2eff3d8fc628ab907d56eb0a9b AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.18@sha256:e3b051cbad561cec1b1ce3586aaf1279aeda72c2416f41c909d293cddf21c011 AS build
WORKDIR /src
COPY ["SaucyBot.csproj", "SaucyBot/"]
RUN dotnet restore "SaucyBot/SaucyBot.csproj"
COPY . SaucyBot/
WORKDIR /src/SaucyBot
RUN dotnet build "SaucyBot.csproj" -c "${CONFIGURATION}" -o /app/build

FROM build AS publish
RUN dotnet publish "SaucyBot.csproj" -c "${CONFIGURATION}" -o /app/publish

FROM base AS final
WORKDIR /app

LABEL org.opencontainers.image.source="https://github.com/Sn0wCrack/saucybot-discord"
LABEL org.opencontainers.image.authors="Sn0wCrack"

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

RUN set -xe; \
    apk add --no-cache --update \
        libc6-compat \
        icu-libs \
        ffmpeg

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SaucyBot.dll"]
