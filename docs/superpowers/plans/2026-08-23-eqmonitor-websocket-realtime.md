# EQMonitor WebSocket Realtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace periodic EQMonitor EEW and earthquake Fetch updates with one registered-device WebSocket connection while retaining one-shot synchronization and HTTP history/detail operations.

**Architecture:** Extend the generated EQMonitor HTTP client with device registration and realtime ticket endpoints, then add a single connection service for registration, ticket acquisition, heartbeat, ordered processing, and reconnect backoff. Existing EEW and earthquake services subscribe to typed realtime events and provide one-shot initial synchronization callbacks.

**Tech Stack:** C# 13, .NET 9, Avalonia, ReactiveUI/System.Reactive, `ClientWebSocket`, `System.Threading.Channels`, Newtonsoft.Json, NSwag 14.6.3, xUnit

**Spec:** `docs/superpowers/specs/2026-08-23-eqmonitor-websocket-realtime-design.md`

## Global Constraints

- Register through `POST /v2/device` with `type: "DESKTOP"` on every OS and `locale: "ja"`.
- Persist only `deviceId` and its normalized registration URL; never persist `deviceToken` or WebSocket tickets.
- Send `EqMonitorApiProvider.UserAgent` and `EqMonitorApiProvider.BuildNumber` on HTTP and WebSocket handshakes.
- Use HTTP only for ready/reconnect synchronization, history, paging, and details. Never poll while disconnected.
- Write user-facing strings, logs, errors, and comments in Japanese.
- Never use real production URLs or hostnames in tests.
- Do not hand-edit OpenAPI JSON or generated NSwag source.

---

### Task 1: Generate Device and Realtime Contracts

**Files:**
- Modify: `scripts/update-eqmonitor-openapi.py`
- Generate: `src/KyoshinEewViewer.EqMonitorApi/openapi.json`
- Create: `tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeApiClientTests.cs`

**Interfaces:**
- Produces: generated `PostV2DeviceAsync`, `GetV2RealtimeTicketAsync`, `DeviceRegisterBody`, `DeviceRegisterResponse`, `DeviceType`, `DeviceLocale`, and `RealtimeTicketResponse`
- Preserves: generated `Earthquake` and `EewItemWithRelations` for WebSocket record parsing

- [ ] **Step 1: Write failing generated-client tests**

Use the existing stub handler pattern and safe hosts. Assert that `PostV2DeviceAsync` sends `POST /v2/device` with `DESKTOP` and `ja`, and that `GetV2RealtimeTicketAsync` parses a safe URL such as `wss://websocketexample/v2/realtime/ws?ticket=test`.

```csharp
var response = await client.PostV2DeviceAsync(new Generated.DeviceRegisterBody
{
    Type = Generated.DeviceType.DESKTOP,
    Locale = Generated.DeviceLocale.Ja,
});
Assert.Equal("registered-device", response.DeviceId);
Assert.Contains("\"type\":\"DESKTOP\"", handler.Body);
```

- [ ] **Step 2: Run the tests to verify RED**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeApiClientTests'
```

Expected: compile failure because the generated endpoints and types are absent.

- [ ] **Step 3: Extend normalization roots**

Keep only the registration and ticket paths by exact match in addition to the existing earthquake/EEW prefixes. A `/v2/device` prefix would accidentally generate all notification-setting APIs. Seed component reachability with `Earthquake` and `EewItemWithRelations`, since realtime components are not necessarily referenced from an HTTP response.

```python
KEEP_PREFIXES = ("/v2/earthquake", "/v2/eew")
KEEP_PATHS = {"/v2/device", "/v2/realtime/ticket", "/v2/realtime/example"}
KEEP_SCHEMA_ROOTS = {"Earthquake", "EewItemWithRelations"}
```

- [ ] **Step 4: Regenerate and verify GREEN**

```bash
./scripts/update-eqmonitor-openapi.py
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeApiClientTests|FullyQualifiedName~EqMonitorApiClientTests|FullyQualifiedName~EqMonitorEewHistoryApiClientTests'
```

Expected: regeneration succeeds and new plus existing API tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/update-eqmonitor-openapi.py src/KyoshinEewViewer.EqMonitorApi/openapi.json tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeApiClientTests.cs
git commit -m "feat: EQMonitor Realtime API契約を生成"
```

---

### Task 2: Register and Persist the Desktop Device

**Files:**
- Modify: `src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs`
- Modify: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorApiProvider.cs`
- Create: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorDeviceService.cs`
- Create: `tests/KyoshinEewViewer.Tests/Services/EqMonitorDeviceServiceTests.cs`
- Modify: `tests/KyoshinEewViewer.Tests/Services/EqMonitorApiClientTests.cs`

**Interfaces:**
- Produces: `Task<string> GetOrRegisterDeviceIdAsync(CancellationToken)`
- Produces: `void InvalidateRegistration()`
- Produces: `Uri? EqMonitorApiProvider.GetBaseUri()`

- [ ] **Step 1: Write failing registration tests**

Consolidate unregistered, same-host reuse, host change, and invalidation. Inject a no-op save callback and assert one save per registration.

```csharp
var first = await service.GetOrRegisterDeviceIdAsync(CancellationToken.None);
var second = await service.GetOrRegisterDeviceIdAsync(CancellationToken.None);
Assert.Equal(first, second);
Assert.Equal("DESKTOP", handler.RegisteredType);
Assert.Equal("ja", handler.RegisteredLocale);
Assert.Equal(1, handler.RegisterCount);
```

Also assert HTTP `User-Agent`, `x-eqmonitor-build`, and device header behavior.

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorDeviceServiceTests|FullyQualifiedName~EqMonitorApiClientTests'
```

- [ ] **Step 3: Add registration configuration**

Add nullable reactive `DeviceId` and `DeviceRegisteredBaseUrl` properties. Keep polling properties until Task 6 so the removal test has a genuine RED phase.

- [ ] **Step 4: Apply device headers only to the matching host**

Expose the resolved base URI. Track applied base URI and device ID; rebuild `HttpClient` when either changes. Add `x-eqmonitor-device-id` only when stored registration URL equals the normalized current URL. Always retain User-Agent and build headers.

- [ ] **Step 5: Implement semaphore-protected registration**

Use generated enums and discard the returned token:

```csharp
var response = await client.PostV2DeviceAsync(new Generated.DeviceRegisterBody
{
    Type = Generated.DeviceType.DESKTOP,
    Locale = Generated.DeviceLocale.Ja,
}, cancellationToken);
Config.EqMonitor.DeviceId = response.DeviceId;
Config.EqMonitor.DeviceRegisteredBaseUrl = baseUri.AbsoluteUri;
SaveConfiguration(Config);
```

Production persistence calls `ConfigurationLoader.Save`; an internal constructor accepts `Action<KyoshinEewViewerConfiguration>` for tests.

- [ ] **Step 6: Verify GREEN and commit**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorDeviceServiceTests|FullyQualifiedName~EqMonitorApiClientTests'
git add src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorApiProvider.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorDeviceService.cs tests/KyoshinEewViewer.Tests/Services/EqMonitorDeviceServiceTests.cs tests/KyoshinEewViewer.Tests/Services/EqMonitorApiClientTests.cs
git commit -m "feat: EQMonitorへDesktop端末を登録"
```

---

### Task 3: Parse Typed WebSocket Messages

**Files:**
- Create: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeMessage.cs`
- Create: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeMessageParser.cs`
- Create: `tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeMessageParserTests.cs`

**Interfaces:**
- Produces: `EqMonitorRealtimeMessage Parse(string json)`
- Produces: ready, ping, pong, EEW upsert, earthquake upsert/delete, and unsupported message records

- [ ] **Step 1: Write failing parser tests**

Cover control messages in a theory, then valid generated EEW and earthquake records, earthquake delete, unknown type, and malformed JSON.

```csharp
[Theory]
[InlineData("{\"type\":\"ready\"}", typeof(EqMonitorReadyMessage))]
[InlineData("{\"type\":\"ping\"}", typeof(EqMonitorPingMessage))]
[InlineData("{\"type\":\"pong\",\"pingId\":\"p1\"}", typeof(EqMonitorPongMessage))]
public void 制御メッセージを判別できる(string json, Type expected)
    => Assert.IsType(expected, EqMonitorRealtimeMessageParser.Parse(json));
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeMessageParserTests'
```

- [ ] **Step 3: Define focused transport records**

```csharp
public abstract record EqMonitorRealtimeMessage;
public sealed record EqMonitorReadyMessage : EqMonitorRealtimeMessage;
public sealed record EqMonitorPingMessage : EqMonitorRealtimeMessage;
public sealed record EqMonitorPongMessage(string? PingId) : EqMonitorRealtimeMessage;
public sealed record EqMonitorEewUpsertMessage(Generated.EewItemWithRelations Record) : EqMonitorRealtimeMessage;
public sealed record EqMonitorEarthquakeUpsertMessage(Generated.Earthquake Record) : EqMonitorRealtimeMessage;
public sealed record EqMonitorEarthquakeDeleteMessage(string EventId) : EqMonitorRealtimeMessage;
public sealed record EqMonitorUnsupportedMessage(string Type, string? Operation) : EqMonitorRealtimeMessage;
```

- [ ] **Step 4: Implement parse-once discrimination**

Parse a `JObject`, branch on outer `type`, then `data.type` and `data.operation`. Deserialize only `data.record` into generated records. Missing fields for known messages throw `JsonSerializationException`; unknown combinations return unsupported.

- [ ] **Step 5: Verify GREEN and commit**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeMessageParserTests'
git add src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeMessage.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeMessageParser.cs tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeMessageParserTests.cs
git commit -m "feat: EQMonitor Realtimeメッセージを解析"
```

---

### Task 4: Own and Reconnect One WebSocket

**Files:**
- Create: `src/KyoshinEewViewer/Services/EqMonitor/IEqMonitorWebSocket.cs`
- Create: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorClientWebSocket.cs`
- Create: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeService.cs`
- Create: `tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeServiceTests.cs`

**Interfaces:**
- Produces: idempotent `Start()` and `Dispose()`
- Produces: `RegisterEewConsumer(Func<CancellationToken, Task>, Action<Generated.EewItemWithRelations>, Action<bool>)`
- Produces: `RegisterEarthquakeConsumer(Func<CancellationToken, Task>, Action<EqMonitorRealtimeMessage>, Action<bool>)`

- [ ] **Step 1: Write failing fake-socket integration tests**

Use an inbound channel and captured headers. Assert one socket for both consumers, ready synchronization before queued business events, immediate pong, User-Agent/build handshake headers, reconnect delays capped at 60 seconds, one invalid-device retry, malformed messages staying nonfatal, and configuration disable cancellation.

```csharp
service.RegisterEewConsumer(
    _ => { order.Add("sync"); return Task.CompletedTask; },
    _ => order.Add("event"),
    _ => { });
service.Start();
fake.Receive("{\"type\":\"ready\"}");
fake.Receive(validEewEnvelope);
Assert.Equal(["sync", "event"], order);
Assert.Equal(EqMonitorApiProvider.UserAgent, fake.Headers["User-Agent"]);
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeServiceTests'
```

- [ ] **Step 3: Implement the production socket adapter**

Wrap `ClientWebSocket`, apply headers before connect, reassemble fragmented UTF-8 text frames, send text, close normally, and record connect/send/receive/disconnect through `NetworkDebugRecorder` so ticket query values are sanitized.

- [ ] **Step 4: Implement receiver and ordered processor loops**

Use a per-connection `Channel<EqMonitorRealtimeMessage>`. The receiver answers ping immediately and queues ready/business messages. The processor awaits enabled initial-sync callbacks when it dequeues ready, then processes later queued business events in arrival order.

```csharp
private static TimeSpan GetReconnectDelay(int retryCount) =>
    TimeSpan.FromSeconds(Math.Min(1 << Math.Min(retryCount, 6), 60));
```

Reset retries after ready. On ticket 401/404, invalidate and register once before normal backoff.

- [ ] **Step 5: Implement configuration lifecycle**

Observe global enable, EEW enable, earthquake enable, and BaseUrl. Connect only if global plus at least one consumer flag is enabled. Cancel immediately on disable or endpoint change. A consumer registered while ready performs its one-shot sync before subsequent messages of that type.

- [ ] **Step 6: Verify GREEN and commit**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeServiceTests'
git add src/KyoshinEewViewer/Services/EqMonitor/IEqMonitorWebSocket.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorClientWebSocket.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorRealtimeService.cs tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeServiceTests.cs
git commit -m "feat: EQMonitor WebSocketへ自動再接続"
```

---

### Task 5: Replace EEW and Earthquake Polling

**Files:**
- Modify: `src/KyoshinEewViewer/Series/KyoshinMonitor/Services/Eew/EqMonitorEewSubscriber.cs`
- Modify: `src/KyoshinEewViewer/Series/KyoshinMonitor/RealtimeEarthquakeInformationHost.cs`
- Modify: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorEarthquakeService.cs`
- Modify: `src/KyoshinEewViewer/Series/Earthquake/Services/EarthquakeWatchService.cs`
- Modify: `src/KyoshinEewViewer/Series/Earthquake/EarthquakeSeries.cs`
- Modify: `src/KyoshinEewViewer/Series/KyoshinMonitor/KyoshinMonitorSeries.cs`
- Create: `tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeConsumerTests.cs`

**Interfaces:**
- Consumes: Task 4 consumer registrations
- Produces: one-shot EEW and earthquake synchronization callbacks
- Produces: `bool RemoveExternalEarthquake(string eventId)`

- [ ] **Step 1: Write failing consumer tests**

Cover new/higher versus duplicate/lower EEW serials and cancellation in one test. Cover earthquake upsert/delete and ensure delete removes an EQMonitor-only event but preserves a telegram-backed event. Assert timer ticks make zero EQMonitor HTTP calls.

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeConsumerTests'
```

- [ ] **Step 3: Convert `EqMonitorEewSubscriber`**

Remove timer subscription, polling lock, and last-poll time. Register initial `/v2/eew/latest`, realtime record, and connection-state callbacks. Reuse one record method with `TimerService.CurrentTime`, serial dedupe, `Cancelled`, and `Update`.

- [ ] **Step 4: Convert `EqMonitorEarthquakeService`**

Remove timer start/subscription and last-fetch fields. Rename periodic fetch to `SynchronizeAsync(CancellationToken)` used only by ready/reconnect. Add `EqMonitorEarthquakeConverter.ToFragment(Generated.Earthquake item)` and use it for full realtime records, preserving signature cache, restore, paging cursor, load-more, and detail retrieval.

- [ ] **Step 5: Implement safe delete**

Under `EarthquakesLock`, return false when absent or when any fragment has a non-null `BasedTelegram`; otherwise remove the EQMonitor-only event. Remove the service cache entry before calling this method.

- [ ] **Step 6: Wire one singleton and verify GREEN**

Inject the same lazy singleton into both consumer paths. Register callbacks before idempotent `Start()`.

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitorRealtimeConsumerTests|FullyQualifiedName~EqMonitorEewConverterTests|FullyQualifiedName~EqMonitorEarthquakeConverterTests'
```

- [ ] **Step 7: Commit**

```bash
git add src/KyoshinEewViewer/Series/KyoshinMonitor src/KyoshinEewViewer/Services/EqMonitor/EqMonitorEarthquakeService.cs src/KyoshinEewViewer/Series/Earthquake tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeConsumerTests.cs
git commit -m "feat: EQMonitor更新をWebSocketへ移行"
```

---

### Task 6: Remove Polling Settings and Verify

**Files:**
- Modify: `src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs`
- Modify: `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorPage.axaml`
- Modify: `tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeConsumerTests.cs`

**Interfaces:**
- Removes: `EewPollingIntervalMs` and `EarthquakePollingIntervalMs`
- Preserves: `EarthquakeFetchCount`

- [ ] **Step 1: Remove configuration and UI after the Task 5 behavioral test is GREEN**

The Task 5 test that advances timer ticks and observes zero HTTP requests is the regression test for this removal. Delete both properties, both interval setting expanders, and the EEW polling-delay description. Do not add a WebSocket toggle because it is the only realtime transport.

- [ ] **Step 2: Verify no periodic symbols remain**

```bash
rg -n 'EewPollingIntervalMs|EarthquakePollingIntervalMs|PollAsync|_lastPolledAt|_lastFetchedAt' src tests
```

Expected: no matches.

- [ ] **Step 3: Run feature tests and builds**

```bash
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitor'
dotnet build src/KyoshinEewViewer/KyoshinEewViewer.csproj
dotnet build src/KyoshinEewViewer.Desktop/KyoshinEewViewer.Desktop.csproj
git diff --check
```

Expected: all tests/builds pass and no whitespace errors.

- [ ] **Step 4: Commit and re-run final verification**

```bash
git add src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs src/KyoshinEewViewer/Services/EqMonitor/EqMonitorPage.axaml tests/KyoshinEewViewer.Tests/Services/EqMonitorRealtimeConsumerTests.cs
git commit -m "refactor: EQMonitorの定期Fetch設定を削除"
dotnet test tests/KyoshinEewViewer.Tests/KyoshinEewViewer.Tests.csproj --filter 'FullyQualifiedName~EqMonitor'
dotnet build src/KyoshinEewViewer/KyoshinEewViewer.csproj
dotnet build src/KyoshinEewViewer.Desktop/KyoshinEewViewer.Desktop.csproj
git --no-pager status --short
```

Expected: tests and builds pass; only the user's pre-existing untracked `.claude/worktrees/` may remain.
