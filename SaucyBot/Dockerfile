ARG CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:9.0-alpine3.20@sha256:317ac615fc521f15a81fc983eabe602a5d9bc3970e230a3ec38f15cfb1752ecc AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine3.20@sha256:823a26bb53762a51795dbcdcf67659360d6f62823f5a83dc380cbef226d4ded3 AS build
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
