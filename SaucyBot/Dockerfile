ARG CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.20@sha256:3d283961eaa1db5ea1cfa793b687bf9e4611bfb5c077d1969533b26653bd9130 AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.20@sha256:80bcbcdd7b0f66932911c1b3df5dd0eaecab3acc1fd37688cc3e81704d7422df AS build
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
