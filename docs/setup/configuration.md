Configuration Reference
==========

> A complete reference for all configuration values in SaucyBot.

Environment Variables (.env)
----------

The `.env` file is only used with container-based setups (Docker, Podman, etc.). Standalone installations do not use it.

### Docker Production

The recommended production values. Copy `.env.example` and update as needed.

| Key Name | Value Type | Description | Recommended Value |
|---|---|---|---|
| `DOTNET_ENVIRONMENT` | String | .NET runtime environment | `Production` |
| `CONFIGURATION` | String | Build configuration for Docker | `Release` |
| `MARIADB_USER` | String | MariaDB application user | `bot` |
| `MARIADB_PASSWORD` | String | Password for MariaDB user | *(leave empty)* |
| `MARIADB_RANDOM_ROOT_PASSWORD` | Boolean | Generate a random root password automatically | `true` |
| `MARIADB_DATABASE` | String | Database name to create on startup | `bot` |
| `MARIADB_AUTO_UPGRADE` | Boolean | Run database upgrades on start | `true` |

### Docker Development

The defaults from `.env.example` for local Docker development.

| Key Name | Value Type | Description | Default Value |
|---|---|---|---|
| `DOTNET_ENVIRONMENT` | String | .NET runtime environment | `Development` |
| `CONFIGURATION` | String | Build configuration for Docker | `Debug` |
| `MYSQL_USER` | String | MariaDB application user | `root` |
| `MYSQL_PASSWORD` | String | Password for MariaDB user | `secret` |
| `MARIADB_ROOT_PASSWORD` | String | Root password for MariaDB | `secret` |
| `MARIADB_DATABASE` | String | Database name to create on startup | `bot` |

App Configuration (appsettings.json)
----------

Open `appsettings.json` in a text editor and configure each section as needed.

### Bot

| Key | Value Type | Description                                                                                              | Default |
|---|---|----------------------------------------------------------------------------------------------------------|---|
| `DiscordToken` | String | Your Discord bot token from the [Discord Developer Portal](https://discord.com/developers/applications). | *(empty)* |
| `DisabledSites` | List of Strings | Site names to disable. Add a site name here if you do not intend to use it.                              | `[]` |
| `MaximumEmbeds` | Integer | Maximum number of images to embed per message.                                                           | `8` |
| `ShardMode` | String | Sharding mode for the bot.                                                                               | `Automatic` |
| `DiscordStatus` | Object | Status display configuration. See sub-keys below.                                                        | |
| `DiscordStatus.Enabled` | Boolean | Whether the bot displays a custom status.                                                                | `true` |
| `DiscordStatus.Type` | String | Status activity type.                                                                                    | `Watching` |
| `DiscordStatus.Text` | String | Status text displayed by the bot.                                                                        | `your links...` |
| `MessageCacheSize` | Integer | Number of messages to keep in the cache.                                                                 | `10` |
| `ConnectionTimeout` | Integer | Connection timeout in milliseconds.                                                                      | `120000` |
| `RestrictNSFW` | Boolean | Restricts SaucyBot to only post NSFW content in a NSFW channel. Disables slash command for DMs.          | false

### Database

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Disabled` | Boolean | Disable all database features. When enabled, no database connection is created and per-guild configuration is ignored. | `false` |
| `ConnectionString` | String | MariaDB connection string. Update `user`, `password`, and `database` to match your setup. | |

For Docker:

```
server=database;user=root;password=secret;database=bot
```

For standalone (local MariaDB):

```
server=localhost;user=bot;password=secret;database=bot
```

> [!NOTE]
> The `user` and `password` values in the connection string must match your MariaDB user credentials. When using Docker production, ensure they match the values in your `.env` file.

### Cache

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Driver` | String | Cache driver to use: `Redis` or `Memory`. | `Redis` |
| `Redis.ConnectionString` | String | Valkey/Redis endpoint. | `cache:6379` |
| `Redis.DefaultLifetime` | Integer | Cache entry lifetime in seconds. | `3600` |
| `Memory.DefaultLifetime` | Integer | Cache entry lifetime in seconds when using the memory driver. | `3600` |

### Queue

The production Compose stack connects the bot to the dedicated, internal `queue`
service at `queue:6379`. Queue data is a Valkey stream and is not persisted across
queue restarts.

| Key | Value Type | Description | Default |
|---|---|---|---|
| `ConnectionString` | String | Valkey endpoint, including credentials when required. | `queue:6379` |
| `StreamName` | String | Valkey stream containing queued message work. | `saucybot:messages` |
| `ConsumerGroup` | String | Consumer group used by workers. | `saucybot-workers` |
| `RetryDelay` | TimeSpan | Delay before retrying an unavailable queue operation. | `00:00:01` |
| `ClearPendingOnStartup` | Boolean | Delete the queue stream on startup. Use only when intentionally discarding pending work. | `false` |
| `MessageWorkerCount` | Integer | Number of message workers. | `5` |
| `InteractionWorkerCount` | Integer | Number of interaction workers. | `5` |
| `InteractionChannelCapacity` | Integer | Capacity of the interaction work channel. | `100` |
| `ShutdownDrainTimeout` | TimeSpan | Maximum time allowed to drain work during shutdown. | `00:00:30` |

Queue settings can be overridden without editing the ignored runtime appsettings file.
Use .NET environment variable names such as `Queue__ConnectionString`,
`Queue__StreamName`, and `Queue__ClearPendingOnStartup` in `.env`. This is also the
recommended place for connection-string credentials.

### OpenTelemetry

Use the tracked `SaucyBot/telemetry.example.json` file as the safe starting point for
telemetry values. The application binds the following environment variable overrides:

| Configuration key | Environment variable | Description |
|---|---|---|
| `OpenTelemetry:Enabled` | `OpenTelemetry__Enabled` | Enables metrics and exporters. |
| `OpenTelemetry:ServiceName` | `OpenTelemetry__ServiceName` | Resource service name. |
| `OpenTelemetry:OtlpEndpoint` | `OpenTelemetry__OtlpEndpoint` | OTLP exporter endpoint. |
| `OpenTelemetry:OtlpProtocol` | `OpenTelemetry__OtlpProtocol` | `Grpc` or `HttpProtobuf`. |
| `OpenTelemetry:OtlpHeaders` | `OpenTelemetry__OtlpHeaders` | Comma-separated OTLP headers. Keep credentials out of tracked files. |
| `OpenTelemetry:ExportIntervalMilliseconds` | `OpenTelemetry__ExportIntervalMilliseconds` | Periodic metrics export interval. |
| `OpenTelemetry:Tracing:Enabled` | `OpenTelemetry__Tracing__Enabled` | Enables sampled tracing. |
| `OpenTelemetry:Tracing:SamplingRatio` | `OpenTelemetry__Tracing__SamplingRatio` | Trace sampling ratio from `0` to `1`. |

When enabled, verify that the configured collector receives `saucybot.queue.depth`,
`saucybot.queue.age`, and worker activity metrics. See the [telemetry guide](telemetry.md)
for the complete safe configuration example.

### Sites

Each site has its own configuration block. Only configure the sites you intend to use.

#### ArtStation

| Key | Value Type | Description | Default |
|---|---|---|---|
| `PostLimit` | Integer | Number of images to embed. | `5` |

#### Pixiv

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Login` | String | Pixiv account login. | *(empty)* |
| `Password` | String | Pixiv account password. | *(empty)* |
| `SessionCookie` | String | Pixiv session cookie for authentication. | *(empty)* |
| `PostLimit` | Integer | Number of images to embed. | `5` |
| `UgoiraFormat` | String | Output format for ugoira animations. | `mp4` |
| `UgoiraBitrate` | Integer | Bitrate for ugoira video encoding. | `2000` |

#### Twitter

| Key | Value Type | Description | Default |
|---|---|---|---|
| `ApiKey` | String | Twitter API key. | *(empty)* |
| `ApiSecret` | String | Twitter API secret. | *(empty)* |
| `AccessToken` | String | Twitter access token. | *(empty)* |
| `AccessSecret` | String | Twitter access token secret. | *(empty)* |
| `BearerToken` | String | Twitter bearer token. | *(empty)* |
| `Delay` | String | Delay between API requests in seconds. | `2.00` |

#### ExHentai

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Cookies.MemberId` | String | ExHentai member ID cookie. | *(empty)* |
| `Cookies.PasswordHash` | String | ExHentai password hash cookie. | *(empty)* |

#### FxTwitter

| Key | Value Type | Description | Default |
|---|---|---|---|
| `AutoDetectLanguage` | Boolean | Automatically detect post language. | `false` |

#### DeviantArt

| Key | Value Type | Description | Default |
|---|---|---|---|
| `ClientId` | Integer | DeviantArt API client ID. | `0` |
| `ClientSecret` | String | DeviantArt API client secret. | *(empty)* |

#### Misskey

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Delay` | String | Delay between API requests in seconds. | `2.00` |

#### Bluesky

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Delay` | String | Delay between API requests in seconds. | `2.00` |

#### FurAffinity

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Cookies.A` | String | FurAffinity `a` cookie value. | *(empty)* |
| `Cookies.B` | String | FurAffinity `b` cookie value. | *(empty)* |

Value Types
----------

The following value types are used throughout this reference.

| Type | Description |
|---|---|
| **String** | A textual value enclosed in quotes. |
| **Integer** | A whole number (no quotes). |
| **Boolean** | `true` or `false`. |
| **List of Strings** | A comma-separated list of values, e.g. `["DeviantArt", "e621"]`. |
