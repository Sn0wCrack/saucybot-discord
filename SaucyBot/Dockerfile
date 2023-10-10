ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine3.18@sha256:a63202c78ca82ff12fad9b6f8c9d8d16338e37ef5bacb3d0cebafd6cdff01050 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.18@sha256:38f4916cfd8902b3c703f03aac547790621c0adc474f4cc46e21461653d94774 AS build
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
