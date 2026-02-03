using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.Auth;
using NatsWebSocket.Protocol;
using NatsWebSocket.Subscriptions;
using NatsWebSocket.Transport;

namespace NatsWebSocket
{
    /// <summary>
    /// NATS client over WebSocket. Provides publish, subscribe, and request-reply
    /// with automatic reconnection and NKEY/JWT authentication.
    /// </summary>
    public sealed class NatsConnection : INatsConnection
    {
        // Frozen copies of options (taken at construction time)
        private readonly string _url;
        private readonly INatsAuthHandler _authHandler;
        private readonly string _name;
        private readonly TimeSpan _connectTimeout;
        private readonly TimeSpan _requestTimeout;
        private readonly bool _allowReconnect;
        private readonly int _maxReconnectAttempts;
        private readonly TimeSpan _reconnectDelay;
        private readonly TimeSpan _maxReconnectDelay;
        private readonly bool _reconnectJitter;
        private readonly bool _headers;
        private readonly bool _noResponders;
        private readonly int _receiveBufferSize;
        private readonly TimeSpan _pingInterval;
        private readonly int _maxPingOut;

        private readonly SubscriptionManager _subManager;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<NatsMsg>> _pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<NatsMsg>>();
        private readonly ConcurrentQueue<TaskCompletionSource<bool>> _pongWaiters =
            new ConcurrentQueue<TaskCompletionSource<bool>>();

        private volatile ITransport _transport;
        private ProtocolReader _reader;
        private CancellationTokenSource _cts;
        private Task _readTask;
        private Task _pingTask;

        private NatsStatus _status = NatsStatus.Disconnected;
        private NatsServerInfo _serverInfo;

        // Inbox optimization: single inbox subscription per connection
        private string _inboxPrefix;
        private long _requestCounter;

        private readonly object _statusLock = new object();
        private readonly object _connectionLock = new object();
        private readonly object _jitterLock = new object();
        private readonly Random _jitterRandom = new Random();

        // PING keep-alive tracking
        private int _outstandingPings;

        // Transport factory for creating new transports (overridable for testing)
        private readonly Func<ITransport> _transportFactory;

        public NatsConnection(NatsConnectionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.Url))
                throw new ArgumentException("Url is required", nameof(options));

            // Freeze all option values
            _url = options.Url;
            _authHandler = options.AuthHandler;
            _name = options.Name;
            _connectTimeout = options.ConnectTimeout;
            _requestTimeout = options.RequestTimeout;
            _allowReconnect = options.AllowReconnect;
            _maxReconnectAttempts = options.MaxReconnectAttempts;
            _reconnectDelay = options.ReconnectDelay;
            _maxReconnectDelay = options.MaxReconnectDelay;
            _reconnectJitter = options.ReconnectJitter;
            _headers = options.Headers;
            _noResponders = options.NoResponders;
            _receiveBufferSize = options.ReceiveBufferSize;
            _pingInterval = options.PingInterval;
            _maxPingOut = options.MaxPingOut;
            _subManager = new SubscriptionManager(OnError);
            _transportFactory = () => new WebSocketTransport();
        }

        // Internal constructor for testing with a transport factory
        internal NatsConnection(NatsConnectionOptions options, Func<ITransport> transportFactory) : this(options)
        {
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        }

        public NatsStatus Status
        {
            get { lock (_statusLock) return _status; }
        }

        public NatsServerInfo ServerInfo => _serverInfo;

        public event EventHandler<NatsStatusEventArgs> StatusChanged;
        public event EventHandler<NatsErrorEventArgs> Error;

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            // Guard against double-connect
            lock (_statusLock)
            {
                if (_status == NatsStatus.Connected || _status == NatsStatus.Connecting)
                    throw new InvalidOperationException($"Cannot connect: connection is already {_status}");
            }

            SetStatus(NatsStatus.Connecting);

            try
            {
                lock (_connectionLock)
                {
                    if (_transport == null)
                        _transport = _transportFactory();
                    _reader = new ProtocolReader(_receiveBufferSize);
                }

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(_connectTimeout);
                    await PerformHandshakeAsync(timeoutCts.Token).ConfigureAwait(false);
                }

                // Set up inbox subscription for request-reply
                lock (_connectionLock)
                {
                    _inboxPrefix = "_INBOX." + Guid.NewGuid().ToString("N") + ".";
                }

                _cts = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));

                // Subscribe to inbox wildcard
                var inboxSid = _subManager.NextSid();
                await SendAsync(ProtocolWriter.Sub(_inboxPrefix + "*", inboxSid), ct).ConfigureAwait(false);

                // Start PING keep-alive
                _outstandingPings = 0;
                _pingTask = Task.Run(() => PingLoopAsync(_cts.Token));

                SetStatus(NatsStatus.Connected);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetStatus(NatsStatus.Disconnected);
                throw;
            }
            catch (OperationCanceledException)
            {
                SetStatus(NatsStatus.Disconnected);
                throw new NatsConnectionException($"Connection to {_url} timed out after {_connectTimeout.TotalSeconds}s");
            }
            catch (Exception ex) when (!(ex is NatsException))
            {
                SetStatus(NatsStatus.Disconnected);
                throw new NatsConnectionException($"Failed to connect to {_url}: {ex.Message}", ex);
            }
        }

        public async Task PublishAsync(string subject, byte[] data, NatsHeaders headers = null, CancellationToken ct = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            EnsureConnected();
            if (data == null) data = Array.Empty<byte>();

            byte[] msg;
            if (headers != null)
            {
                var hdrBytes = headers.ToWireBytes();
                msg = ProtocolWriter.HPub(subject, null, hdrBytes, data);
            }
            else
            {
                msg = ProtocolWriter.Pub(subject, null, data);
            }

            await SendAsync(msg, ct).ConfigureAwait(false);
        }

        public async Task<NatsMsg> RequestAsync(string subject, byte[] data, NatsHeaders headers = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            EnsureConnected();
            if (data == null) data = Array.Empty<byte>();

            var effectiveTimeout = timeout ?? _requestTimeout;
            var reqId = Interlocked.Increment(ref _requestCounter).ToString();
            var replyTo = _inboxPrefix + reqId;

            var tcs = new TaskCompletionSource<NatsMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[replyTo] = tcs;

            try
            {
                // Build and send the publish command with reply-to
                byte[] msg;
                if (headers != null)
                {
                    var hdrBytes = headers.ToWireBytes();
                    msg = ProtocolWriter.HPub(subject, replyTo, hdrBytes, data);
                }
                else
                {
                    msg = ProtocolWriter.Pub(subject, replyTo, data);
                }

                await SendAsync(msg, ct).ConfigureAwait(false);

                // Wait for reply with timeout
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(effectiveTimeout);
                    var registration = timeoutCts.Token.Register(() =>
                        tcs.TrySetException(new NatsRequestTimeoutException(subject, effectiveTimeout)));

                    try
                    {
                        var reply = await tcs.Task.ConfigureAwait(false);

                        // Check for No Responders
                        if (reply.IsNoResponders)
                            throw new NatsNoRespondersException(subject);

                        return reply;
                    }
                    finally
                    {
                        registration.Dispose();
                    }
                }
            }
            finally
            {
                _pendingRequests.TryRemove(replyTo, out _);
            }
        }

        public Task<NatsSubscription> SubscribeAsync(string subject, Action<NatsMsg> handler, CancellationToken ct = default)
        {
            return SubscribeAsync(subject, null, handler, ct);
        }

        public async Task<NatsSubscription> SubscribeAsync(string subject, string queueGroup, Action<NatsMsg> handler, CancellationToken ct = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            EnsureConnected();

            var state = _subManager.Add(subject, queueGroup, handler);

            var cmd = ProtocolWriter.Sub(subject, state.Sid, queueGroup);
            await SendAsync(cmd, ct).ConfigureAwait(false);

            return new NatsSubscription(state.Sid, subject, sid => Unsubscribe(sid));
        }

        public Task<NatsSubscription> SubscribeAsync(string subject, Func<NatsMsg, Task> handler, CancellationToken ct = default)
        {
            return SubscribeAsync(subject, null, handler, ct);
        }

        public async Task<NatsSubscription> SubscribeAsync(string subject, string queueGroup, Func<NatsMsg, Task> handler, CancellationToken ct = default)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            EnsureConnected();

            var state = _subManager.AddAsync(subject, queueGroup, handler);

            var cmd = ProtocolWriter.Sub(subject, state.Sid, queueGroup);
            await SendAsync(cmd, ct).ConfigureAwait(false);

            return new NatsSubscription(state.Sid, subject, sid => Unsubscribe(sid));
        }

        public async Task FlushAsync(CancellationToken ct = default)
        {
            EnsureConnected();

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pongWaiters.Enqueue(tcs);

            await SendAsync(ProtocolWriter.Ping(), ct).ConfigureAwait(false);

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(_connectTimeout);
                var registration = timeoutCts.Token.Register(() =>
                    tcs.TrySetException(new NatsConnectionException("Flush timed out waiting for PONG")));

                try
                {
                    await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    registration.Dispose();
                }
            }
        }

        public async Task CloseAsync(CancellationToken ct = default)
        {
            lock (_statusLock)
            {
                if (_status == NatsStatus.Closed)
                    return;
            }

            SetStatus(NatsStatus.Closed);
            _cts?.Cancel();

            // Fail all pending requests
            FailPendingRequests(new NatsConnectionException("Connection closed"));
            FailPongWaiters(new NatsConnectionException("Connection closed"));

            var transport = _transport;
            if (transport != null)
            {
                try
                {
                    await transport.CloseAsync(ct).ConfigureAwait(false);
                }
                catch { /* best effort */ }
            }

            if (_readTask != null)
            {
                try { await _readTask.ConfigureAwait(false); }
                catch { /* expected */ }
            }

            if (_pingTask != null)
            {
                try { await _pingTask.ConfigureAwait(false); }
                catch { /* expected */ }
            }
        }

        public void Dispose()
        {
            lock (_statusLock)
            {
                if (_status == NatsStatus.Closed)
                    return;
                _status = NatsStatus.Closed;
            }

            // Cancel background tasks without awaiting to avoid sync-over-async deadlock.
            // Tasks will exit via CTS cancellation and transport disposal.
            // For graceful shutdown, call CloseAsync() before Dispose().
            try { _cts?.Cancel(); }
            catch { /* best effort */ }

            FailPendingRequests(new ObjectDisposedException(nameof(NatsConnection)));
            FailPongWaiters(new ObjectDisposedException(nameof(NatsConnection)));

            _transport?.Dispose();
            _cts?.Dispose();
        }

        #region Handshake

        private async Task PerformHandshakeAsync(CancellationToken ct)
        {
            var transport = _transport;
            var uri = new Uri(_url);
            await transport.ConnectAsync(uri, ct).ConfigureAwait(false);

            // Read INFO from server
            var infoLine = await ReadLineAsync(ct).ConfigureAwait(false);
            if (!infoLine.StartsWith("INFO "))
                throw new NatsConnectionException($"Expected INFO from server, got: {infoLine}");

            var internalInfo = JsonReader.ParseServerInfo(infoLine.Substring(5));
            _serverInfo = NatsServerInfo.FromInternal(internalInfo);

            // Authenticate
            string jwt = null, sig = null, authToken = null, user = null, pass = null, nkey = null;

            if (_authHandler != null)
            {
                var authResult = _authHandler.Authenticate(internalInfo.Nonce);
                jwt = authResult.Jwt;
                sig = authResult.Signature;
                authToken = authResult.AuthToken;
                user = authResult.User;
                pass = authResult.Pass;
                nkey = authResult.Nkey;
            }

            // Build and send CONNECT
            var connectJson = ConnectCommand.Build(
                name: _name,
                verbose: false,
                pedantic: false,
                headers: _headers,
                noResponders: _noResponders,
                jwt: jwt,
                signature: sig,
                authToken: authToken,
                user: user,
                pass: pass,
                nkey: nkey);

            await SendAsync(ProtocolWriter.Connect(connectJson), ct).ConfigureAwait(false);
            await SendAsync(ProtocolWriter.Ping(), ct).ConfigureAwait(false);

            // Expect PONG (or +OK then PONG)
            var response = await ReadLineAsync(ct).ConfigureAwait(false);
            if (response == "+OK")
                response = await ReadLineAsync(ct).ConfigureAwait(false);

            if (response != "PONG")
            {
                if (response.StartsWith("-ERR"))
                {
                    var errMsg = response.Length > 5 ? response.Substring(5).Trim().Trim('\'') : response;
                    if (errMsg.Contains("Authorization") || errMsg.Contains("auth") || errMsg.Contains("Auth"))
                        throw new NatsAuthException($"Authentication failed: {errMsg}");
                    throw new NatsServerException(errMsg);
                }
                throw new NatsConnectionException($"Expected PONG after CONNECT, got: {response}");
            }
        }

        /// <summary>
        /// Read a single CRLF-terminated line during handshake (before read loop starts).
        /// </summary>
        private async Task<string> ReadLineAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var parsed = _reader.TryParse();
                if (parsed != null)
                {
                    switch (parsed.Command)
                    {
                        case "INFO":
                            return "INFO " + parsed.RawLine;
                        case "PONG":
                            return "PONG";
                        case "+OK":
                            return "+OK";
                        case "-ERR":
                            return "-ERR " + parsed.RawLine;
                        case "PING":
                            await SendAsync(ProtocolWriter.Pong(), ct).ConfigureAwait(false);
                            continue;
                        default:
                            continue;
                    }
                }

                var transport = _transport;
                var count = await transport.ReceiveAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (count == 0)
                    throw new NatsConnectionException("WebSocket closed during handshake");

                _reader.Append(buffer, 0, count);
            }
        }

        #endregion

        #region Read Loop

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[_receiveBufferSize];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var transport = _transport;
                    if (transport == null || !transport.IsConnected)
                        break;

                    int count;
                    try
                    {
                        count = await transport.ReceiveAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        OnError(ex);
                        break;
                    }

                    if (count == 0)
                        break; // WebSocket closed

                    _reader.Append(buffer, 0, count);

                    // Process all complete messages
                    ParsedMsg parsed;
                    while ((parsed = _reader.TryParse()) != null)
                    {
                        await HandleParsedMessageAsync(parsed, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                OnError(ex);
            }

            // If not intentionally closed, attempt reconnect
            if (_status != NatsStatus.Closed && _allowReconnect)
            {
                await ReconnectLoopAsync().ConfigureAwait(false);
            }
            else if (_status != NatsStatus.Closed)
            {
                FailPendingRequests(new NatsConnectionException("Connection lost"));
                FailPongWaiters(new NatsConnectionException("Connection lost"));
                SetStatus(NatsStatus.Disconnected);
            }
        }

        private async Task HandleParsedMessageAsync(ParsedMsg parsed, CancellationToken ct)
        {
            switch (parsed.Command)
            {
                case "PING":
                    try { await SendAsync(ProtocolWriter.Pong(), ct).ConfigureAwait(false); }
                    catch { /* best effort */ }
                    break;

                case "PONG":
                    Interlocked.Exchange(ref _outstandingPings, 0);
                    // Signal flush waiters
                    if (_pongWaiters.TryDequeue(out var waiter))
                        waiter.TrySetResult(true);
                    break;

                case "+OK":
                    break;

                case "-ERR":
                    var serverErr = new NatsServerException(parsed.RawLine ?? "Unknown error");
                    OnError(serverErr);
                    break;

                case "INFO":
                    // Server may send updated INFO (e.g. new routes). Update cached info.
                    if (parsed.RawLine != null)
                    {
                        var internalInfo = JsonReader.ParseServerInfo(parsed.RawLine);
                        _serverInfo = NatsServerInfo.FromInternal(internalInfo);
                    }
                    break;

                case "MSG":
                case "HMSG":
                    DispatchMessage(parsed);
                    break;
            }
        }

        private void DispatchMessage(ParsedMsg parsed)
        {
            // Check if this is a reply to a pending request (inbox)
            if (parsed.Subject != null && _inboxPrefix != null && parsed.Subject.StartsWith(_inboxPrefix))
            {
                if (_pendingRequests.TryRemove(parsed.Subject, out var tcs))
                {
                    var msg = new NatsMsg
                    {
                        Subject = parsed.Subject,
                        Sid = parsed.Sid,
                        ReplyTo = parsed.ReplyTo,
                        Data = parsed.Payload ?? Array.Empty<byte>()
                    };

                    if (parsed.HeaderBytes != null && parsed.HeaderBytes.Length > 0)
                        msg.Headers = NatsHeaders.FromWireBytes(parsed.HeaderBytes, 0, parsed.HeaderBytes.Length);

                    tcs.TrySetResult(msg);
                    return;
                }
            }

            // Dispatch to subscription handler
            _subManager.Dispatch(parsed);
        }

        #endregion

        #region PING Keep-Alive

        private async Task PingLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_pingInterval, ct).ConfigureAwait(false);

                    if (ct.IsCancellationRequested) break;

                    var missed = Interlocked.Increment(ref _outstandingPings);
                    if (missed > _maxPingOut)
                    {
                        OnError(new NatsConnectionException($"Server unresponsive: {missed} PINGs without PONG"));
                        // Force disconnect so the read loop triggers reconnect
                        var transport = _transport;
                        if (transport != null)
                        {
                            try { await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
                            catch { /* best effort */ }
                        }
                        break;
                    }

                    try
                    {
                        await SendAsync(ProtocolWriter.Ping(), ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Transport failed — read loop will handle reconnect
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        #endregion

        #region Reconnection

        private async Task ReconnectLoopAsync()
        {
            SetStatus(NatsStatus.Reconnecting);
            FailPendingRequests(new NatsConnectionException("Connection lost, reconnecting"));
            FailPongWaiters(new NatsConnectionException("Connection lost, reconnecting"));

            var delay = _reconnectDelay;
            var attempt = 0;

            while (_status == NatsStatus.Reconnecting)
            {
                if (_maxReconnectAttempts >= 0 && attempt >= _maxReconnectAttempts)
                {
                    SetStatus(NatsStatus.Disconnected);
                    return;
                }

                attempt++;

                // Wait with exponential backoff + optional jitter
                var actualDelay = delay;
                if (_reconnectJitter)
                {
                    double jitterOffset;
                    lock (_jitterLock)
                    {
                        var jitter = actualDelay.TotalMilliseconds * 0.25;
                        jitterOffset = (_jitterRandom.NextDouble() * 2 - 1) * jitter;
                    }
                    actualDelay = TimeSpan.FromMilliseconds(actualDelay.TotalMilliseconds + jitterOffset);
                }

                try { await Task.Delay(actualDelay).ConfigureAwait(false); }
                catch (TaskCanceledException) { return; }

                if (_status != NatsStatus.Reconnecting)
                    return;

                try
                {
                    // Clean up old transport
                    lock (_connectionLock)
                    {
                        _transport?.Dispose();
                        _transport = _transportFactory();
                        _reader = new ProtocolReader(_receiveBufferSize);
                    }

                    using (var timeoutCts = new CancellationTokenSource(_connectTimeout))
                    {
                        await PerformHandshakeAsync(timeoutCts.Token).ConfigureAwait(false);
                    }

                    // Re-subscribe inbox
                    lock (_connectionLock)
                    {
                        _inboxPrefix = "_INBOX." + Guid.NewGuid().ToString("N") + ".";
                    }
                    var inboxSid = _subManager.NextSid();
                    await SendAsync(ProtocolWriter.Sub(_inboxPrefix + "*", inboxSid), CancellationToken.None).ConfigureAwait(false);

                    // Re-subscribe all active subscriptions
                    var resubCmds = _subManager.GetResubscribeCommands();
                    foreach (var cmd in resubCmds)
                    {
                        await SendAsync(cmd, CancellationToken.None).ConfigureAwait(false);
                    }

                    // Cancel and await old ping loop before disposing CTS
                    var oldCts = _cts;
                    oldCts?.Cancel();
                    if (_pingTask != null)
                    {
                        try { await _pingTask.ConfigureAwait(false); }
                        catch { /* expected during shutdown */ }
                    }
                    oldCts?.Dispose();

                    // Restart read loop and ping loop
                    _cts = new CancellationTokenSource();
                    _outstandingPings = 0;
                    _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
                    _pingTask = Task.Run(() => PingLoopAsync(_cts.Token));

                    SetStatus(NatsStatus.Connected);
                    return;
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }

                // Exponential backoff
                delay = TimeSpan.FromMilliseconds(Math.Min(
                    delay.TotalMilliseconds * 2,
                    _maxReconnectDelay.TotalMilliseconds));
            }
        }

        #endregion

        #region Helpers

        private void Unsubscribe(string sid)
        {
            _subManager.Remove(sid);
            // Fire-and-forget the UNSUB send to avoid blocking in Dispose
            var cmd = ProtocolWriter.Unsub(sid);
            Task.Run(async () =>
            {
                try { await SendAsync(cmd, CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
            });
        }

        private async Task SendAsync(byte[] data, CancellationToken ct)
        {
            var transport = _transport;
            if (transport == null || !transport.IsConnected)
                throw new NatsConnectionException("Not connected");

            await transport.SendAsync(data, ct).ConfigureAwait(false);
        }

        private void EnsureConnected()
        {
            var status = Status;
            if (status != NatsStatus.Connected)
                throw new NatsConnectionException($"Not connected (status: {status})");
        }

        private void SetStatus(NatsStatus newStatus)
        {
            lock (_statusLock)
            {
                if (_status == newStatus) return;
                _status = newStatus;
            }
            StatusChanged?.Invoke(this, new NatsStatusEventArgs(newStatus));
        }

        private void OnError(Exception ex)
        {
            Error?.Invoke(this, new NatsErrorEventArgs(ex));
        }

        private void FailPendingRequests(Exception ex)
        {
            foreach (var kvp in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                    tcs.TrySetException(ex);
            }
        }

        private void FailPongWaiters(Exception ex)
        {
            while (_pongWaiters.TryDequeue(out var waiter))
            {
                waiter.TrySetException(ex);
            }
        }

        #endregion
    }
}
