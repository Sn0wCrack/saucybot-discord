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

| Key | Value Type | Description                                                   | Default |
|---|---|---------------------------------------------------------------|---|
| `Driver` | String | Cache driver to use: `redis`, `hybrid` or `memory`.           | `Memory` |
| `Redis.ConnectionString` | String | Valkey/Redis server connection string.                        | `cache:6379` |
| `Redis.DefaultLifetime` | Integer | Cache entry lifetime in seconds.                              | `3600` |
| `Memory.DefaultLifetime` | Integer | Cache entry lifetime in seconds when using the memory driver. | `3600` |

### Queue

Queue uses a separate Redis-compatible database, such as Redis or Valkey, for storing incoming message work.

Use the `Driver` key to select the queue backend. Redis is the default backend.

The `Queue` keys below are generic worker settings. They apply to every backend. Connection strings, stream names, consumer groups, and Redis command settings live under `Queue.Redis`.

```json
{
  "Queue": {
    "Driver": "Redis",
    "MessageWorkerCount": 5,
    "Redis": {
      "ConnectionString": "queue:6379"
    }
  }
}
```

| Key | Value Type | Description | Default |
|---|---|---|---|
| `Driver` | String | Queue driver to use. | `Redis` |
| `EnqueueTimeout` | TimeSpan | Maximum time an enqueue call waits for the backend before it reports a timeout. Environment variable: `Queue__EnqueueTimeout`. The backend can still accept the item after the caller stops waiting, so callers do not retry automatically. | `00:00:05` |
| `BackendOperationTimeout` | TimeSpan | Maximum time one backend operation, such as lease renewal, completion, retry, or cleanup, may take. Environment variable: `Queue__BackendOperationTimeout`. | `00:00:05` |
| `MaxProcessingAttempts` | Integer | Maximum processing attempts before a failed message is acknowledged and deleted. | `3` |
| `MaxProcessingTime` | TimeSpan | Maximum time a worker may process one item before it gives up ownership for recovery. | `00:05:00` |
| `HeartbeatInterval` | TimeSpan | How often an active worker renews its delivery lease. Environment variable: `Queue__HeartbeatInterval`. Keep this shorter than `PendingMessageIdleTime`. | `00:00:05` |
| `PendingMessageIdleTime` | TimeSpan | Minimum idle time before an unfinished delivery can be recovered after its worker stops renewing the lease. Environment variable: `Queue__PendingMessageIdleTime`. | `00:00:30` |
| `ReclaimerInterval` | TimeSpan | How often the recovery worker scans for abandoned deliveries. Environment variable: `Queue__ReclaimerInterval`. | `00:00:05` |
| `MessageWorkerCount` | Integer | Number of message workers. Increase only after checking queue age, CPU, memory, and upstream rate limits. | `5` |
| `InteractionWorkerCount` | Integer | Number of interaction workers. | `5` |
| `InteractionChannelCapacity` | Integer | Maximum number of admitted in-process interactions waiting for workers. | `100` |
| `RecoveryHandoffCapacity` | Integer | Maximum number of recovered deliveries waiting in the in-process handoff before recovery pauses. Environment variable: `Queue__RecoveryHandoffCapacity`. | `25` |
| `ClearPendingOnStartup` | Boolean | Delete pending work when the queue starts. Enable only when intentionally discarding pending work. | `false` |
| `ShutdownDrainTimeout` | TimeSpan | Maximum time allowed to drain admitted work during shutdown. | `00:00:30` |

> [!WARNING]
> The timing keys `HeartbeatInterval`, `PendingMessageIdleTime`, and `ReclaimerInterval` moved from `Queue:Redis` to `Queue`.
> The legacy keys `Queue__Redis__HeartbeatInterval`, `Queue__Redis__PendingMessageIdleTime`, and `Queue__Redis__ReclaimerInterval` still work when the matching generic key is not set, and the bot logs a deprecation warning for each one it uses.
> Move these values to `Queue__HeartbeatInterval`, `Queue__PendingMessageIdleTime`, and `Queue__ReclaimerInterval`. A generic key always takes precedence over its legacy key.

#### Timeout and outage behavior

Queue operations are bounded, so a Redis outage cannot block the bot without limit.

An enqueue stops after `EnqueueTimeout`. If Redis does not answer in time, the bot reports a timeout, records the `saucybot.queue.enqueue_timed_out` metric, and logs a warning. The bot does not retry the enqueue automatically. Redis can still accept the item after the bot stops waiting, so the same message can enter the queue later.

Lease renewal, completion, retry, and cleanup stop after `BackendOperationTimeout`. The Redis command timeout follows the same value, so Redis also stops each command at the transport level. If a completion or a retry has an unknown outcome, the item stays in the queue and recovery delivers it again later. The handler does not run again in the same attempt.

Message processing is at-least-once. Handlers must tolerate duplicate delivery and must give the same result when they run twice. Handlers must honor cancellation and must stop side effects when the token is canceled.

> [!WARNING]
> A handler that ignores cancellation keeps its worker until it finishes. Lease renewal stops at `MaxProcessingTime`, so recovery can deliver the item to another worker and the side effects then run twice. If all workers are stuck on such handlers, restart the bot. The queue logs each handler that runs past its deadline.

#### Queue.Redis

These keys are specific to the Redis backend. A different backend validates and documents its own settings.

Lease scripts use only the configured `StreamName` key. Redis Cluster routes each script by that key, so `StreamName` does not need a hash tag.

> [!WARNING]
> Do not run old and new queue workers at the same time during a rollout. Old workers do not use lease tokens. They can change ownership or delete work after a new worker claims it.

| Key | Value Type | Description | Default |
|---|---|---|---|
| `ConnectionString` | String | Valkey/Redis endpoint, including credentials when required. | `queue:6379` |
| `StreamName` | String | Redis/Valkey stream containing queued message work. | `saucybot:messages` |
| `ConsumerGroup` | String | Consumer group used by message workers. | `saucybot-workers` |
| `RetryDelay` | TimeSpan | Delay before retrying an unavailable Redis operation. | `00:00:01` |
| `PendingReadTimeout` | TimeSpan | Maximum wait for a Redis read to finish during cancellation. | `00:00:01` |
| `MalformedCleanupMaxAttempts` | Integer | Maximum cleanup attempts for malformed queue entries. | `3` |
| `MalformedCleanupMaxDelay` | TimeSpan | Maximum delay between malformed-entry cleanup attempts. | `00:00:05` |

### OpenTelemetry

OpenTelemetry configuration allows for sending debugging metrics to an OTLP compatible server.

| Configuration key | Environment variable | Description |
|---|---|---|
| `OpenTelemetry:Enabled` | `OpenTelemetry__Enabled` | Enables metrics and exporters. Defaults to `false`. |
| `OpenTelemetry:ServiceName` | `OpenTelemetry__ServiceName` | Resource service name. |
| `OpenTelemetry:OtlpEndpoint` | `OpenTelemetry__OtlpEndpoint` | OTLP exporter endpoint. |
| `OpenTelemetry:OtlpProtocol` | `OpenTelemetry__OtlpProtocol` | `Grpc` or `HttpProtobuf`. |
| `OpenTelemetry:OtlpHeaders` | `OpenTelemetry__OtlpHeaders` | Comma-separated OTLP headers, such as `api-key=secret`. |
| `OpenTelemetry:ExportIntervalMilliseconds` | `OpenTelemetry__ExportIntervalMilliseconds` | Periodic metrics export interval. |
| `OpenTelemetry:Tracing:Enabled` | `OpenTelemetry__Tracing__Enabled` | Enables sampled tracing. |
| `OpenTelemetry:Tracing:SamplingRatio` | `OpenTelemetry__Tracing__SamplingRatio` | Trace sampling ratio from `0` to `1`. |

### Sentry

Sentry collects error reports from the bot at runtime. Leave `Dsn` empty to disable Sentry.

| Configuration key | Environment variable | Description |
|---|---|---|
| `Sentry:Dsn` | `Sentry__Dsn` | Sentry project DSN. Empty string disables Sentry. |
| `Sentry:SampleRate` | `Sentry__SampleRate` | Fraction of events to report, from `0` to `1`. |

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

#### FxTwitter (alternate implementation)

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
