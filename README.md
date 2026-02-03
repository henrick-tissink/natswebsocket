# NatsWebSocket

NATS client over WebSocket for .NET Framework 4.6.2+ and .NET Standard 2.0.

Fills the gap where the official [NATS.Client](https://github.com/nats-io/nats.net) v1.x has no WebSocket support and [NATS.Net](https://github.com/nats-io/nats.net.v2) v2.x requires .NET 6+.

## Features

- WebSocket transport (ws:// and wss://)
- Publish, Subscribe, Request-Reply
- NATS headers (HPUB/HMSG)
- NKEY + JWT authentication (.creds file support)
- Token and user/password authentication
- Automatic reconnection with exponential backoff + jitter
- PING keep-alive with configurable interval and max missed PONGs
- Single inbox optimization for request-reply
- Zero JSON dependencies (minimal internal JSON reader/writer)
- Only dependency: BouncyCastle.Cryptography (for Ed25519 NKEY signing)

## Installation

```
dotnet add package NatsWebSocket
```

Or reference the project directly:

```xml
<ProjectReference Include="path/to/NatsWebSocket.csproj" />
```

## Quick Start

```csharp
using NatsWebSocket;
using NatsWebSocket.Auth;

var conn = new NatsConnection(new NatsConnectionOptions
{
    Url = "wss://my-nats-server:443",
    AuthHandler = new NKeyAuthHandler("path/to/creds"),
});

await conn.ConnectAsync();

// Request-Reply
var headers = new NatsHeaders();
headers.Add("token", jwt);
var reply = await conn.RequestAsync("svc.user.create", payload, headers);
Console.WriteLine(reply.GetString());

// Subscribe (async)
var sub = await conn.SubscribeAsync("notifications.>", msg =>
    Console.WriteLine($"[{msg.Subject}] {msg.GetString()}"));

// Publish
await conn.PublishAsync("events.update", data);

// Cleanup
sub.Dispose();
await conn.CloseAsync(); // always call CloseAsync before Dispose for graceful shutdown
conn.Dispose();
```

## Authentication

### NKEY + JWT (.creds file)

```csharp
var auth = new NKeyAuthHandler("path/to/file.creds");
```

### NKEY + JWT (raw values)

```csharp
var auth = new NKeyAuthHandler(jwtString, nkeySeedBytes);
```

### Token

```csharp
var auth = new TokenAuthHandler("my-secret-token");
```

### User/Password

```csharp
var auth = new UserPasswordAuthHandler("user", "pass");
```

## Connection Options

| Option | Default | Description |
|--------|---------|-------------|
| `Url` | *(required)* | WebSocket URL (ws:// or wss://) |
| `AuthHandler` | `null` | Authentication handler |
| `Name` | `"NatsWebSocket"` | Client name in CONNECT |
| `ConnectTimeout` | 10s | Handshake timeout |
| `RequestTimeout` | 30s | Default request-reply timeout |
| `AllowReconnect` | `true` | Auto-reconnect on disconnect |
| `MaxReconnectAttempts` | `-1` (unlimited) | Max reconnect tries |
| `ReconnectDelay` | 1s | Initial reconnect delay |
| `MaxReconnectDelay` | 30s | Maximum reconnect delay |
| `ReconnectJitter` | `true` | +/-25% random jitter |
| `Headers` | `true` | Advertise header support |
| `NoResponders` | `true` | Request 503 status headers |
| `ReceiveBufferSize` | 65536 | WebSocket receive buffer |
| `PingInterval` | 30s | Keep-alive PING interval |
| `MaxPingOut` | 3 | Max missed PONGs before disconnect |

## Events

```csharp
conn.StatusChanged += (s, e) => Console.WriteLine($"Status: {e.Status}");
conn.Error += (s, e) => Console.WriteLine($"Error: {e.Exception.Message}");
```

## Shutdown

Always call `CloseAsync()` before `Dispose()`. `CloseAsync` performs a graceful WebSocket close handshake and waits for background tasks to exit. `Dispose` is a synchronous safety net that tears down resources without waiting.

```csharp
await conn.CloseAsync();
conn.Dispose();
```

## Build

```sh
dotnet build
dotnet test
dotnet pack -c Release
```

## Target Framework

The library targets `netstandard2.0`, making it compatible with:
- .NET Framework 4.6.2+
- .NET Core 2.0+
- .NET 5+

## License

MIT
