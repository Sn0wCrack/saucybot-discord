# Testing

## Default tests

The normal test command runs unit tests and composition tests without starting containers:

```shell
dotnet test SaucyBot.slnx --configuration Release
```

Redis integration tests are marked with the xUnit `Category=Integration` trait. The repository uses xUnit's Microsoft Testing Platform runner, so the default full run discovers those tests but self-skips them before creating a container unless the integration category is selected. They use Testcontainers with a disposable Valkey container and do not require Discord credentials or external API access.

Run the Redis integration suite explicitly with:

```shell
dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --configuration Release --filter Category=Integration
```

The suite uses the production-pinned image `docker.io/valkey/valkey:9-alpine@sha256:a174b894902bd3367e330d47cc2054367dc4917701776aaf336f41d83b65ec7a`. Testcontainers starts it with the production queue settings: 512 MiB `maxmemory`, `noeviction`, snapshots disabled, and append-only persistence disabled. Testcontainers removes the container after the fixture completes. If the selected container runtime is unavailable, the integration tests report a clear skip instead of failing the normal unit suite.

## Docker

Docker Desktop is supported on Windows, macOS, and Linux. On Linux, install Docker Engine and ensure the daemon is running. Testcontainers discovers the local Docker API using its standard Docker endpoint settings.

Verify the runtime before running integration tests:

```shell
docker version
```

If Docker Desktop is installed but stopped, start it and retry the filtered command. If the daemon is remote or uses a non-default socket, configure the standard Testcontainers/Docker endpoint settings before running the tests.

## Podman

Podman is supported when its Docker-compatible API service is running. Start the user socket on Linux with:

```shell
systemctl --user enable --now podman.socket
```

Point Docker-compatible clients, including Testcontainers, at the Podman socket for the current shell. The exact socket path can be obtained with:

```shell
podman info --format '{{.Host.RemoteSocket.Path}}'
```

Set `DOCKER_HOST` to the returned Unix socket URI, for example:

```shell
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
```

Then verify the service and run the filtered test command:

```shell
podman info
dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --configuration Release --filter Category=Integration
```

Testcontainers talks to the Docker-compatible API; it does not require a separate SaucyBot setting. On systems where Podman is configured through a machine or remote connection, use the endpoint exposed by that connection.

## Troubleshooting

- `neither Docker nor Podman is available`: install one runtime, start its daemon or socket, and rerun the `Category=Integration` command.
- `permission denied` for the Docker or Podman socket: add the current user to the runtime's access group or configure the user socket, then start a new shell.
- image pull failures: verify registry access and that `docker.io/valkey/valkey:9-alpine` can be pulled by the selected runtime.
- stale containers after an interrupted test run: list and remove only the Testcontainers resources using the selected runtime's normal cleanup commands. Do not remove unrelated application containers.
- integration tests are not discovered: confirm the command includes `--filter Category=Integration` and that the test project was restored after package changes.
