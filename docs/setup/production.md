Production Setup
==========

> Setting up SaucyBot for production using a container runtime with Compose support.

Prerequisites
----------

- [Git](https://git-scm.com/)
- A container runtime with Compose support, such as:
  - [Docker](https://docs.docker.com/get-docker/) (includes Docker Compose)
  - [Podman](https://podman.io/) with [Podman Compose](https://github.com/containers/podman-compose) or Docker Compose

> [!CAUTION]
> Ensure the full file path to your working directory contains no spaces.

Step 1: Clone the repository
----------

```shell
git clone https://github.com/Sn0wCrack/saucybot-discord.git
cd saucybot-discord
```

Step 2: Copy configuration files
----------

From the `SaucyBot` directory, copy the production template files into place:

```shell
cd SaucyBot
cp compose.prod.yml compose.yml
cp .env.example .env
cp appsettings.json.example appsettings.json
```

Step 3: Configure the bot
----------

Follow the [Configuration Reference](configuration.md) to set up your `.env` and `appsettings.json` files.

For production, use the **Docker Production** `.env` values and set `DotnetEnvironment` to `Production`.

Step 4: Start the bot
----------

From the `SaucyBot` directory, run:

```shell
docker compose up -d
```

This will build the bot image and start four containers:

| Service | Image | Exposed Port |
|---|---|---|
| `bot` | Built from `SaucyBot/` | — |
| `database` | `mariadb:12.3` | 3306 |
| `cache` | `valkey/valkey:9-alpine` | 6379 |
| `queue` | `valkey/valkey:9-alpine` | — |

Step 5: Verify running containers
----------

```shell
docker compose ps
```

All four services (`bot`, `database`, `cache`, `queue`) should show status `Up` or `running`.

Before starting or after changing Compose files, validate the fully resolved configuration:

```shell
docker compose -f compose.prod.yml config
```

The cache and queue are separate Valkey instances. Cache data is persistent, but queue
data is intentionally not: the queue has no volume and uses `noeviction`, disabled RDB
saves, and disabled AOF. The queue has no host port; the bot reaches it at `queue:6379`.
The bot also has no `/tmp` tmpfs mount, so its temporary files remain disk-backed in the
container writable layer.

Step 6: View logs
----------

```shell
docker compose logs -f
```

Append a service name to view logs for a single container:

```shell
docker compose logs -f bot
docker compose logs -f database
docker compose logs -f cache
docker compose logs -f queue
```

### Operational checks

Check queue memory and server state from inside the queue container:

```shell
docker compose exec queue valkey-cli INFO memory
docker compose exec queue valkey-cli INFO persistence
docker compose exec queue valkey-cli XINFO STREAM saucybot:messages
docker compose exec queue valkey-cli XPENDING saucybot:messages saucybot-workers
```

Use `XINFO STREAM` and `XPENDING` to monitor stream length, oldest entry age, and
consumer-group backlog. OpenTelemetry exports `saucybot.queue.depth` and
`saucybot.queue.age` when enabled. Configure the exporter with the
`OpenTelemetry__OtlpEndpoint` and related environment variables described in the
[telemetry guide](telemetry.md); keep endpoint credentials in `.env`, not tracked files.

Do not treat separate logical Valkey databases as resource isolation. Cache and queue
must remain separate Valkey services so their memory limits, eviction policies, and
restart behavior are independent.

Database auto-migration
----------

When `MARIADB_AUTO_UPGRADE=true` is set in your `.env`, MariaDB will automatically apply any required database schema changes on startup. No manual migration steps are needed.
