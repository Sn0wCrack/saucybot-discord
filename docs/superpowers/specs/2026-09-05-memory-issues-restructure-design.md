# Memory Issues Restructure Design

**Goal:** Prevent unbounded Discord event work and avoidable media allocations while preserving message processing, adding observable runtime behavior, and making database-backed operation mandatory.

**Approved architecture:** Discord events are converted into compact work envelopes and processed by tracked workers through a dedicated transient Valkey Stream. The queue Valkey instance is separate from the cache instance, uses a 512 MiB `noeviction` budget, and does not need to survive restarts. Site processing continues through `SiteManager`, `ProcessRequest`, `ProcessResponse`, and `MessageManager`; Discord-specific live-message behavior is isolated behind processing context abstractions.

**Resource ownership:** `ProcessResponse` owns returned attachment streams and is disposed after all sends complete. HTTP response messages, HTTP content streams, archives, temporary media files, and attachment streams have explicit ownership and disposal paths. NSFW policy is available in `ProcessRequest` so sites can classify before downloading media.

**Observability:** OpenTelemetry is the primary telemetry system. Runtime metrics, queue metrics, processing metrics, and download metrics are configurable through `appsettings.json` and environment overrides. Tracing is optional and sampled. Sentry remains suitable for exceptions and selected traces.

**Database:** Database registration, migrations, guild configuration, and database-dependent commands are always enabled. The runtime `Database:Disabled` switch and null production implementations are removed.

**Operational limits:** Cache Valkey remains separately bounded according to its cache policy. Queue Valkey is capped at 512 MiB with `noeviction`; enqueue backpressure is observable and retried rather than silently evicting work. Bot `/tmp` remains disk-backed rather than RAM-backed.
