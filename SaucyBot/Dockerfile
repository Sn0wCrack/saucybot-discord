ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.19@sha256:6c3def5f3ab3f1f1cdf9320f351b15e200dca527212733dab7eb404bff1dbb21 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.19@sha256:b1275049a8fe922cbc9f1d173ffec044664f30b94e99e2c85dd9b7454fbf596c AS build
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
