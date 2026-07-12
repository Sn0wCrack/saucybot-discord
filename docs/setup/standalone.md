Standalone Setup
==========

> Setting up SaucyBot for local development without Docker.

Prerequisites
----------

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [MariaDB](https://mariadb.org/download/) (12.3 or later)
- [Valkey](https://valkey.io/) or [Redis](https://redis.io/)
- [ffmpeg](https://ffmpeg.org/) (required for Pixiv ugoira video conversion)
- [Git](https://git-scm.com/)

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

From the `SaucyBot` directory, copy the configuration template into place:

```shell
cd SaucyBot
cp appsettings.json.example appsettings.json
```

Step 3: Start MariaDB and Valkey
----------

Ensure MariaDB and Valkey (or Redis) are running locally before starting the bot.

On most Linux distributions you can start them with:

```shell
sudo systemctl start mariadb
sudo systemctl start valkey
```

Or on macOS with Homebrew:

```shell
brew services start mariadb
brew services start valkey
```

> [!NOTE]
> You must create the database and user before starting the bot. For example:
>
> ```shell
> mariadb -u root -p
> CREATE DATABASE bot;
> CREATE USER 'bot'@'localhost' IDENTIFIED BY 'secret';
> GRANT ALL PRIVILEGES ON bot.* TO 'bot'@'localhost';
> FLUSH PRIVILEGES;
> EXIT;
> ```

Step 4: Configure the bot
----------

Follow the [Configuration Reference](configuration.md) to set up your `appsettings.json` file.

For standalone installs, skip the `.env` section — it is only used with Docker.

Step 5: Run the bot
----------

From the repository root, build and run the bot:

```shell
dotnet run --project SaucyBot
```

Or to build and run separately:

```shell
dotnet build SaucyBot
dotnet run --project SaucyBot
```

The bot will automatically apply any pending database schema changes on first start.

Step 6: Verify
----------

Once the bot starts you should see Serilog output in your terminal. If the connection to MariaDB or Valkey fails, double-check that both services are running and that your connection strings in `appsettings.json` are correct.

Database auto-migration
----------

SaucyBot uses Entity Framework Core to manage database schema. For a standalone MariaDB install, no extra configuration is needed — EF Core handles schema creation and migrations automatically on first start.

> [!NOTE]
> The `MARIADB_AUTO_UPGRADE` environment variable is only relevant when running MariaDB inside Docker. For standalone installs, EF Core manages the schema directly via the database provider and the server setting is not required.

ffmpeg requirement
----------

Pixiv ugoira video conversion requires `ffmpeg` to be installed and available on your system PATH. Without it the bot will not be able to convert ugoira animations to video. You can verify your installation by running:

```shell
ffmpeg -version
```
