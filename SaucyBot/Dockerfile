ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.19@sha256:12a01c882e69bf0cdc592ec2083b00a8ca538fccc68ba0578c37e91ade2a5233 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.19@sha256:53af222a293d9d843c966e43feab6167593aeea3ddbac4d45f29e1a463f5c0ca AS build
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
