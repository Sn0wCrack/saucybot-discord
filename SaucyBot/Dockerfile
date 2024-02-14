ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.19@sha256:f7a0f3109c33b56b5fef870dc708b9196e61ab4533fe51779c65e67fde14d195 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.19@sha256:f0b0bf6e7e016bd6fb90da9a7ba4c2ab0c383335066675b17b758a8bdf5b8a15 AS build
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
