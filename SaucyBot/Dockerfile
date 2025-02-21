ARG CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.20@sha256:6981d441f1213c06278bd3217bde4ec4f3b6da4065033ec5a37158ea00e71740 AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.20@sha256:eca9cd7300e8d48de5b0866b2b5f8e087f79c2aca6e02e13d4c411141076cac7 AS build
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
