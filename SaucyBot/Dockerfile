ARG CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:10.0-alpine3.22@sha256:cb52db7383b4365f899aa886de4c18721a9ddee2e86e9d00814d1cb4d5d398c2 AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.22@sha256:bb9890f2f2dbefa91b161feecfe6f85fd4c8c9e3933158c54287f37f611d5ed5 AS build
WORKDIR /src
COPY ["SaucyBot.csproj", "SaucyBot/"]
RUN dotnet restore "SaucyBot/SaucyBot.csproj" -a "${TARGETARCH}"
COPY . SaucyBot/
WORKDIR /src/SaucyBot
RUN dotnet build "SaucyBot.csproj" -c "${CONFIGURATION}" -a "${TARGETARCH}" -o /app/build

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
