ARG CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.19@sha256:15bd72c402b543c962979691e42e4ee72dacd643b9073c1fde37962ad37c4027 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.19@sha256:19f102beec35f56e7abfc3324f2d76cb67823492552c6955be1b521d10d6a73b AS build
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
