Docker Development Setup
==========

> Setting up SaucyBot for local development using a container runtime with Compose support.

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

From the `SaucyBot` directory, copy the development template files into place:

```shell
cd SaucyBot
cp .env.example .env
cp appsettings.Development.json.example appsettings.Development.json
```

Step 3: Configure the bot
----------

Follow the [Configuration Reference](configuration.md) to set up your `.env` and `appsettings.Development.json` files.

For development, use the **Docker Development** `.env` defaults.

Step 4: Start the bot
----------

From the `SaucyBot` directory, run:

```shell
docker compose -f compose.dev.yml up -d
```

This will build the bot image and start three containers:

| Service | Image | Exposed Port |
|---|---|---|
| `bot` | Built from `SaucyBot/` | — |
| `database` | `mariadb:12.3` | 3306 |
| `cache` | `valkey/valkey:9-alpine` | 6379 |

Step 5: Verify running containers
----------

```shell
docker compose -f compose.dev.yml ps
```

All three services (`bot`, `database`, `cache`) should show status `Up` or `running`.

Step 6: View logs
----------

```shell
docker compose -f compose.dev.yml logs -f
```

Append a service name to view logs for a single container:

```shell
docker compose -f compose.dev.yml logs -f bot
docker compose -f compose.dev.yml logs -f database
docker compose -f compose.dev.yml logs -f cache
```

Step 7: Rebuilding after code changes
----------

After modifying bot source code, rebuild and restart the `bot` container:

```shell
docker compose -f compose.dev.yml up -d --build
```

This rebuilds the .NET image with your latest changes and restarts the bot. The `database` and `cache` containers are unaffected.

Differences from production
----------

The development configuration differs from the production setup in several ways:

| | Development | Production |
|---|---|---|
| **Compose file** | `docker-compose.dev.yml` | `docker-compose.prod.yml` (or `docker-compose.yml`) |
| **CONFIGURATION build arg** | Sourced from `.env` (default: `Debug`) | Hardcoded to `Release` |
| **App config file** | `appsettings.Development.json` (mounted) | `appsettings.json` (mounted) |
| **Bot restart policy** | `unless-stopped` | None (default: `no`) |
| **DOTNET_ENVIRONMENT** | `Development` | `Production` |

Database auto-migration
----------

Database schema changes are applied automatically on container startup. No manual migration steps are needed.
