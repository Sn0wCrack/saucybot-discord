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
cp docker-compose.prod.yml docker-compose.yml
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

This will build the bot image and start three containers:

| Service | Image | Exposed Port |
|---|---|---|
| `bot` | Built from `SaucyBot/` | — |
| `database` | `mariadb:12.3` | 3306 |
| `cache` | `valkey/valkey:9-alpine` | 6379 |

Step 5: Verify running containers
----------

```shell
docker compose ps
```

All three services (`bot`, `database`, `cache`) should show status `Up` or `running`.

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
```

Database auto-migration
----------

When `MARIADB_AUTO_UPGRADE=true` is set in your `.env`, MariaDB will automatically apply any required database schema changes on startup. No manual migration steps are needed.
