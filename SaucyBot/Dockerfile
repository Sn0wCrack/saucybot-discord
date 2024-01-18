ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.18@sha256:5bfb631cd5ec5b14e7e7429928e8085c29b56e8950b03906c7f562586bd5a43c AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.18@sha256:1655c8f2784cf17f81191a9fd80fbf2e3f2ee1c2c86f174714635963986cb96b AS build
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
