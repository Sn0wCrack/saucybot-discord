ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine3.18@sha256:69499ed600990444ab94e02d2229378c48caedf4816b531580c6e595bfd77710 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.18@sha256:fbbbdfc34566e2f4fcb2147292b76b9b847911a348c404a7a24b1656465e6bea AS build
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
