# Memory Issues Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent unbounded Discord event work and avoidable media allocations while preserving message processing, adding observable runtime behavior, and making database-backed operation mandatory.

**Architecture:** Discord events are converted into compact work envelopes and processed by tracked workers through a dedicated transient Valkey Stream. The queue Valkey instance is separate from the cache instance and uses a 512 MiB `noeviction` budget. Existing site and response pipelines remain the application flow; Discord-specific message state, cancellation, NSFW policy, and stream ownership are represented at the processing boundary.

**Tech Stack:** .NET 10, Discord.Net 3.20.1, Valkey Streams, StackExchange.Redis, OpenTelemetry, Serilog, xUnit v3, NSubstitute, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-09-05-memory-issues-restructure-design.md`

## Global Constraints

- Do not silently discard queued work while the process is running.
- Queue Valkey must use `maxmemory 512mb` and `maxmemory-policy noeviction`.
- Queue data does not need to survive a bot or queue restart.
- Do not change site-specific processing behavior except where required for message-context access or pre-download NSFW classification.
- Dispose attachment streams only after every Discord send using the response has completed.
- OpenTelemetry must be configurable and disabled without requiring an exporter when disabled.
- Database support is mandatory at runtime; tests use dependency injection doubles instead of a production disable switch.
- Keep secrets and private credentials out of source, image layers, and committed configuration.

---

### Task 1: Define Processing and Message Context Contracts

**Files:**
- Create: `SaucyBot/Site/ProcessingContext.cs`
- Create: `SaucyBot/Site/IMessageContext.cs`
- Create: `SaucyBot/Site/ICommandContext.cs`
- Modify: `SaucyBot/Site/ProcessRequest.cs`
- Modify: `SaucyBot/Services/MessageValidator.cs`
- Modify: `SaucyBot/Services/IGuildConfigurationManager.cs`
- Create: `SaucyBot.Tests/Unit/Site/ProcessRequestTest.cs`

**Interfaces:**
- `ProcessingContext` exposes `CancellationToken CancellationToken`, `bool NsfwAllowed`, `IMessageContext? Message`, and `ICommandContext? Command`.
- `IMessageContext` exposes message/channel/guild identifiers, content, author metadata needed by validation, current embeds, and `Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken)`.
- `ICommandContext` is a live-process adapter for command identifiers, option content, user locale, channel/guild identifiers, and follow-up response operations; it is never serialized into Valkey.
- `ProcessRequest` retains `Match` and `GuildConfiguration?`, replaces direct Discord message/command coupling with `ProcessingContext? Context`, and retains `IsMessage`, `IsSlashCommand`, `UserLocale`, and `Guild` accessors through the context.

- [ ] **Step 1: Write failing contract tests**

Test that a request exposes `NsfwAllowed`, cancellation, locale, message/command classification, and guild metadata from its context.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~ProcessRequestTest`

Expected: FAIL because the new context types and request properties do not exist.

- [ ] **Step 3: Implement the minimal contracts**

Use immutable records/interfaces. Keep compatibility accessors on `ProcessRequest` so site callers do not need unrelated changes.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~ProcessRequestTest`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SaucyBot/Site SaucyBot.Tests/Unit/Site/ProcessRequestTest.cs
git commit -m "refactor: add processing context contracts"
```

### Task 2: Make ProcessResponse Own Resources

**Files:**
- Modify: `SaucyBot/Site/ProcessResponse.cs`
- Modify: `SaucyBot/Common/KnownLengthStream.cs`
- Create: `SaucyBot/Common/HttpResponseStream.cs`
- Modify: `SaucyBot/Site/Twitter/FxTwitter.cs:378-393`
- Modify: `SaucyBot/Library/Sites/Pixiv/PixivClient.cs:137-154`
- Modify: `SaucyBot/Site/Pixiv/Pixiv.cs:82-142`
- Modify: `SaucyBot/Services/SiteManager.cs:127-153`
- Modify: `SaucyBot/Services/MessageManager.cs:21-111`
- Create: `SaucyBot.Tests/Unit/Site/ProcessResponseTest.cs`
- Create: `SaucyBot.Tests/Unit/Common/KnownLengthStreamTest.cs`

**Interfaces:**
- `ProcessResponse : IAsyncDisposable` disposes each owned `FileAttachment.Stream` once.
- `HttpResponseStream : Stream` owns both the response stream and its `HttpResponseMessage`.
- `SiteManager` disposes every non-null response in a `finally` block after sending all partitions.

- [ ] **Step 1: Write failing disposal tests**

Test response disposal for multiple attachments, repeated disposal, send failures, and NSFW responses with no files. Test that disposing `HttpResponseStream` disposes the response and inner stream.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~ProcessResponseTest|FullyQualifiedName~KnownLengthStreamTest'`

Expected: FAIL because ownership and async disposal are not implemented.

- [ ] **Step 3: Implement ownership**

Add idempotent asynchronous disposal. Ensure `MessageManager` completes every send before response disposal. Preserve the existing stream until all partitioned messages have sent.

- [ ] **Step 4: Dispose HTTP responses correctly**

Wrap `HttpResponseMessage` and its content stream in `HttpResponseStream`; return that wrapper from Pixiv and FxTwitter clients. Dispose `ZipArchive` before the downloaded source stream. Replace Pixiv `File.ReadAllBytesAsync` with a read-only `FileStream` for the rendered file.

- [ ] **Step 5: Run focused tests and the site tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~ProcessResponseTest|FullyQualifiedName~KnownLengthStreamTest'`

Expected: PASS. Then run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~Site`

- [ ] **Step 6: Commit**

```bash
git add SaucyBot/Site SaucyBot/Common SaucyBot/Library/Sites SaucyBot/Services SaucyBot.Tests
git commit -m "fix: dispose media response resources"
```

### Task 3: Add OpenTelemetry Configuration and Metrics

**Files:**
- Modify: `SaucyBot/SaucyBot.csproj`
- Modify: `SaucyBot/Program.cs`
- Create: `SaucyBot/Diagnostics/TelemetryOptions.cs`
- Create: `SaucyBot/Diagnostics/SaucyBotMetrics.cs`
- Create: `SaucyBot/Diagnostics/TelemetryServiceRegistration.cs`
- Modify: `SaucyBot/appsettings.json`
- Modify: `SaucyBot/appsettings.Development.json`
- Create: `SaucyBot.Tests/Unit/Diagnostics/TelemetryServiceRegistrationTest.cs`

**Interfaces:**
- `TelemetryOptions` binds `OpenTelemetry:Enabled`, service name, OTLP endpoint/protocol/headers, export interval, and trace enablement/sampling.
- `SaucyBotMetrics` owns meters and instruments for queue depth, queue age, active workers, processing duration, enqueue/dequeue/failure counts, download bytes, and download concurrency.
- `AddSaucyBotTelemetry(IServiceCollection, IConfiguration)` registers no exporters/providers when disabled and configures runtime, HTTP, custom metrics, and optional sampled tracing when enabled.

- [ ] **Step 1: Add package references and configuration-binding tests**

Add OpenTelemetry core, OTLP exporter, hosting, runtime, and HTTP instrumentation packages compatible with .NET 10. Test enabled and disabled configuration binding.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~TelemetryServiceRegistrationTest`

Expected: FAIL until registration exists.

- [ ] **Step 3: Implement telemetry registration and custom instruments**

Subscribe to the built-in `System.Runtime` meter for .NET runtime metrics. Do not create a tracing provider when tracing is disabled. Read OTLP endpoint and headers from configuration so environment variables can override them.

- [ ] **Step 4: Run focused tests and build**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~TelemetryServiceRegistrationTest`

Expected: PASS. Then run: `dotnet build SaucyBot.slnx --configuration Release`

- [ ] **Step 5: Commit**

```bash
git add SaucyBot/SaucyBot.csproj SaucyBot/Program.cs SaucyBot/Diagnostics SaucyBot/appsettings*.json SaucyBot.Tests/Unit/Diagnostics
git commit -m "feat: add configurable OpenTelemetry"
```

### Task 4: Implement Valkey Queue Contracts and Service

**Files:**
- Modify: `SaucyBot/SaucyBot.csproj`
- Create: `SaucyBot/Queue/MessageWorkItem.cs`
- Create: `SaucyBot/Queue/IMessageWorkQueue.cs`
- Create: `SaucyBot/Queue/ValkeyWorkQueue.cs`
- Create: `SaucyBot/Queue/WorkQueueOptions.cs`
- Modify: `SaucyBot/Program.cs`
- Modify: `SaucyBot/appsettings.json`
- Create: `SaucyBot.Tests/Unit/Queue/WorkItemSerializationTest.cs`
- Create: `SaucyBot.Tests/Unit/Queue/ValkeyWorkQueueTest.cs`

**Interfaces:**
- `MessageWorkItem` contains immutable message IDs, guild/channel IDs, content, forwarded content, embeds, author/permission metadata, and a correlation ID.
- `QueuedMessageWorkItem` contains the Valkey stream entry ID and its deserialized `MessageWorkItem`.
- `IMessageWorkQueue.EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken)` applies retryable backpressure.
- `IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(string consumer, CancellationToken cancellationToken)` reads from a Valkey Stream consumer group.
- `Task AcknowledgeAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)` acknowledges only after successful processing.
- `Task ClearPendingAsync(CancellationToken cancellationToken)` clears transient message work on startup when configured.

- [ ] **Step 1: Write serialization and acknowledgement tests**

Test round-tripping every work-item field, preserving correlation IDs, retrying transient Valkey failures, and acknowledging only after successful processing.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~Queue`

Expected: FAIL because queue contracts do not exist.

- [ ] **Step 3: Implement work-item serialization and queue options**

Use explicit JSON serialization options and a versioned stream payload. Do not serialize Discord.NET socket objects or file streams.

- [ ] **Step 4: Implement the Valkey Stream adapter**

Use a dedicated connection string, stream name, consumer group, retry delay, and startup-clear option. Treat `noeviction` write failures as backpressure; never acknowledge an item before processing succeeds.

- [ ] **Step 5: Run tests with a fake queue and build**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~Queue`

Expected: PASS. Run: `dotnet build SaucyBot.slnx --configuration Release`.

- [ ] **Step 6: Commit**

```bash
git add SaucyBot/SaucyBot.csproj SaucyBot/Program.cs SaucyBot/Queue SaucyBot/appsettings*.json SaucyBot.Tests/Unit/Queue
git commit -m "feat: add Valkey work queue"
```

### Task 5: Replace Detached Worker Tasks

**Files:**
- Modify: `SaucyBot/Worker.cs`
- Create: `SaucyBot/Queue/WorkQueueHostedService.cs`
- Create: `SaucyBot/Queue/WorkItemProcessor.cs`
- Create: `SaucyBot/Queue/InteractionWorkChannel.cs`
- Modify: `SaucyBot/Services/SiteManager.cs`
- Modify: `SaucyBot/Services/MessageManager.cs`
- Modify: `SaucyBot/Services/MessageValidator.cs`
- Modify: `SaucyBot/Services/IGuildConfigurationManager.cs`
- Modify: `SaucyBot/Services/InteractionHandler.cs`
- Create: `SaucyBot.Tests/Unit/Queue/WorkItemProcessorTest.cs`
- Create: `SaucyBot.Tests/Unit/WorkerTest.cs`

**Interfaces:**
- `WorkQueueHostedService` starts tracked message workers and drains them during shutdown.
- `InteractionWorkChannel` is a bounded, tracked in-process channel for live interactions. The gateway callback defers the interaction before enqueueing it; interactions are not persisted in Valkey because their follow-up token expires and restart persistence is explicitly unnecessary.
- `WorkItemProcessor.ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)` creates a DI scope, constructs processing context, invokes existing managers, and reports metrics.
- Worker tasks are stored, exceptions are observed, cancellation is passed through all waits and processing calls, and queue items are acknowledged only after successful completion.

- [ ] **Step 1: Write failing worker lifecycle tests**

Test that workers process items, do not create one detached task per event, observe processor exceptions, stop accepting work during shutdown, and wait for active work to finish or cancel.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~WorkItemProcessorTest|FullyQualifiedName~WorkerTest'`

Expected: FAIL because the tracked worker service does not exist.

- [ ] **Step 3: Implement worker lifecycle**

Move message execution out of `Task.Run` event callbacks. Convert ordinary gateway messages into Valkey work items. Defer interactions immediately, place live interactions in the bounded in-process interaction channel, and maintain separate worker limits for messages and interactions. Use `GetByGuildId` for queued messages because they no longer have an `IMessageChannel` object available for configuration lookup.

- [ ] **Step 4: Preserve interaction acknowledgement behavior**

Defer interactions immediately in the gateway callback, then use follow-ups from the live interaction work item. If the bounded channel is full, await admission with cancellation rather than creating another detached task. Do not place Discord interaction objects in Valkey.

- [ ] **Step 5: Add metrics and shutdown draining**

Update queue depth, active worker, age, success, failure, retry, and cancellation instruments. Use the host stopping token and a bounded shutdown drain timeout.

- [ ] **Step 6: Run focused tests and full unit tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~WorkItemProcessorTest|FullyQualifiedName~WorkerTest'`

Expected: PASS. Then run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj`.

- [ ] **Step 7: Commit**

```bash
git add SaucyBot/Worker.cs SaucyBot/Queue SaucyBot/Services SaucyBot.Tests/Unit/Queue SaucyBot.Tests/Unit/WorkerTest.cs
git commit -m "fix: process Discord work through tracked workers"
```

### Task 6: Isolate Live Message and Queued Message Behavior

**Files:**
- Create: `SaucyBot/Site/DiscordMessageContext.cs`
- Create: `SaucyBot/Site/QueuedMessageContext.cs`
- Modify: `SaucyBot/Services/SiteManager.cs`
- Modify: `SaucyBot/Services/MessageManager.cs`
- Modify: `SaucyBot/Services/MessageValidator.cs`
- Modify: `SaucyBot/Services/IGuildConfigurationManager.cs`
- Modify: `SaucyBot/Site/Misskey/Misskey.cs:52-62`
- Modify: `SaucyBot/Site/Bluesky/Bluesky.cs:52-62`
- Modify: `SaucyBot/Queue/WorkItemProcessor.cs`
- Create: `SaucyBot.Tests/Unit/Site/MessageContextTest.cs`
- Modify: `SaucyBot.Tests/Unit/Site/MisskeyTest.cs`
- Modify: `SaucyBot.Tests/Unit/Site/BlueskyTest.cs`

**Interfaces:**
- `DiscordMessageContext` reads the live cached message, waits using the request cancellation token, rereads the cache, and fetches only when no cached message is available.
- `QueuedMessageContext` starts with serialized content/embeds, performs the same delayed latest-embed lookup, and uses one targeted fetch only when needed.
- Misskey and Bluesky consume `request.Context.Message.GetLatestEmbedsAsync(...)` instead of directly depending on a `SocketUserMessage`.

- [ ] **Step 1: Write failing context tests**

Test cached embed updates avoid REST calls, missing cache entries perform one targeted fetch, cancellation interrupts the delay, and queued snapshots preserve initial embeds.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~MessageContextTest|FullyQualifiedName~MisskeyTest|FullyQualifiedName~BlueskyTest'`

Expected: FAIL because the abstractions do not exist.

- [ ] **Step 3: Implement live and queued contexts**

Keep the existing delay semantics, replace arbitrary waits with cancellable delays, and ensure the REST fallback is limited to the message whose embeds are required.

- [ ] **Step 4: Update only the two dependent sites**

Preserve their existing matching and response behavior while changing only embed retrieval through the context.

- [ ] **Step 5: Run focused and full site tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter 'FullyQualifiedName~MessageContextTest|FullyQualifiedName~MisskeyTest|FullyQualifiedName~BlueskyTest'`

Expected: PASS. Then run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~Site`.

- [ ] **Step 6: Commit**

```bash
git add SaucyBot/Site SaucyBot/Services/SiteManager.cs SaucyBot/Site/Misskey SaucyBot/Site/Bluesky SaucyBot/Queue/WorkItemProcessor.cs SaucyBot.Tests/Unit/Site
git commit -m "refactor: isolate queued message context"
```

### Task 7: Propagate Processing Policy and Cancellation

**Files:**
- Modify: `SaucyBot/Site/ProcessRequest.cs`
- Modify: `SaucyBot/Services/SiteManager.cs`
- Modify: `SaucyBot/Queue/WorkItemProcessor.cs`
- Create: `SaucyBot.Tests/Unit/Services/SiteManagerTest.cs`

**Interfaces:**
- `ProcessRequest.Context.NsfwAllowed` represents policy permission, not a content classification result.
- `ProcessRequest.Context.CancellationToken` is available to site implementations without requiring site changes in this task.
- No existing site is changed for NSFW classification in this task; future sites can inspect policy before downloading media.

- [ ] **Step 1: Write failing context-propagation tests**

Test disallowed policy is present in requests and the worker cancellation token reaches the processing context.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~SiteManagerTest`

Expected: FAIL until context propagation exists.

- [ ] **Step 3: Propagate context through message and command processing**

Construct `ProcessingContext` from guild configuration and request type. Set `NsfwAllowed` from the existing guild policy and pass the worker cancellation token. Preserve current response and permission behavior.

- [ ] **Step 4: Run focused and full tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj --filter FullyQualifiedName~SiteManagerTest`

Expected: PASS. Then run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj`.

- [ ] **Step 5: Commit**

```bash
git add SaucyBot/Site SaucyBot/Services SaucyBot.Tests/Unit
git commit -m "feat: propagate processing policy and cancellation"
```

### Task 8: Remove Runtime Database Disable Mode

**Files:**
- Modify: `SaucyBot/Program.cs`
- Modify: `SaucyBot/Services/ServiceRegistration.cs`
- Modify: `SaucyBot/Commands/SettingsCommand.cs`
- Delete: `SaucyBot/Services/NullDatabaseMigrator.cs`
- Delete: `SaucyBot/Services/NullGuildConfigurationManager.cs`
- Modify: `SaucyBot/appsettings.json`
- Modify: `SaucyBot/appsettings.Development.json`
- Modify: tests that use `Database:Disabled`

**Interfaces:**
- Production DI always provides `DatabaseContext`, `DatabaseMigrator`, and `GuildConfigurationManager`.
- Tests replace database services through DI or NSubstitute rather than configuration switches.

- [ ] **Step 1: Update failing/obsolete registration tests**

Remove tests whose purpose is the runtime disabled mode and add a registration test asserting database-backed services are always present.

- [ ] **Step 2: Run the test project and verify expected compile failures**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj`

Expected: tests or production references to the removed switch identify every required update.

- [ ] **Step 3: Remove the conditional registration path**

Always call `AddSaucyBotDatabase`, register database-backed services, and make settings command registration unconditional. Remove the configuration property and null implementations.

- [ ] **Step 4: Run all tests**

Run: `dotnet test SaucyBot.Tests/SaucyBot.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SaucyBot/Program.cs SaucyBot/Services SaucyBot/Commands SaucyBot/appsettings*.json SaucyBot.Tests
git commit -m "refactor: require database services"
```

### Task 9: Configure Compose and Production Documentation

**Files:**
- Modify: `SaucyBot/compose.prod.yml`
- Modify: `SaucyBot/appsettings.json`
- Modify: `docs/setup/production.md`
- Modify: `docs/setup/configuration.md`
- Test: Compose configuration validation command

- [ ] **Step 1: Add the dedicated queue service**

Use the same pinned Valkey image as cache. Do not publish its port. Configure `--maxmemory 512mb --maxmemory-policy noeviction --save "" --appendonly no`, add a healthcheck, and avoid a persistent volume because queue data need not survive queue restarts.

- [ ] **Step 2: Add queue and telemetry configuration**

Add queue connection, stream/group/consumer settings, worker limits, startup-clear behavior, and OpenTelemetry settings. Keep endpoint credentials overridable through environment variables.

- [ ] **Step 3: Document operational checks**

Document `docker compose config`, queue memory, queue age, Valkey `INFO`, OpenTelemetry endpoint setup, and the fact that logical Valkey databases do not provide resource isolation.

- [ ] **Step 4: Validate Compose**

Run: `docker compose -f SaucyBot/compose.prod.yml config`

Expected: valid configuration with separate `cache` and `queue` services and no external queue port.

- [ ] **Step 5: Commit**

```bash
git add SaucyBot/compose*.yml SaucyBot/appsettings*.json docs/setup
git commit -m "ops: configure queue and telemetry services"
```

### Task 10: Full Verification and Controlled Load Test

- [ ] **Step 1: Run formatting and static checks**

Run: `dotnet format SaucyBot.slnx --verify-no-changes`

Expected: PASS or a list of formatting changes to apply before continuing.

- [ ] **Step 2: Run the complete test suite**

Run: `dotnet test SaucyBot.slnx --configuration Release`

Expected: PASS for unit tests and benchmark project compilation.

- [ ] **Step 3: Validate production configuration**

Run: `docker compose -f SaucyBot/compose.prod.yml config`

Expected: PASS with queue and cache separated and bot `/tmp` not mounted as tmpfs.

- [ ] **Step 4: Run a controlled load test**

Measure process working set, managed heap, queue depth, oldest queue age, active workers, Valkey memory, download bytes, GC pauses, and Discord reconnect/rate-limit events while increasing message traffic in stages.

- [ ] **Step 5: Confirm acceptance criteria**

Confirm no unbounded detached task population, no response stream leaks, queue items are not evicted, memory returns toward a stable baseline after load, interaction acknowledgements remain within Discord’s response window, and shutdown observes worker failures.

- [ ] **Step 6: Commit final verification updates**

```bash
git add SaucyBot.Tests.Benchmark docs/superpowers
git commit -m "test: verify memory restructure under load"
```
