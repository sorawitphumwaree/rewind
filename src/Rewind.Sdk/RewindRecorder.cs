using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Rewind.Abstractions;
using Rewind.Protocol;

namespace Rewind.Sdk;

public static class RewindRecorder
{
    private static readonly object Gate = new object();
    private static IRewindRecorder? _state;

    public static InitializationResult Initialize(RewindOptions? options = null)
    {
        lock (Gate)
        {
            if (_state != null)
            {
                return new InitializationResult(InitializationStatus.AlreadyInitialized);
            }

            _state = new RecorderState(options ?? new RewindOptions());
            return new InitializationResult(InitializationStatus.Initialized);
        }
    }

    public static InitializationResult Initialize(IRewindRecorder recorder)
    {
        if (recorder == null)
        {
            throw new ArgumentNullException(nameof(recorder));
        }

        lock (Gate)
        {
            if (_state != null)
            {
                return new InitializationResult(InitializationStatus.AlreadyInitialized);
            }

            _state = recorder;
            return new InitializationResult(InitializationStatus.Initialized);
        }
    }

    public static void SetContext(string key, string value) => _state?.SetContext(key, value);
    public static bool RemoveContext(string key) => _state?.RemoveContext(key) ?? false;
    public static void ClearContext() => _state?.ClearContext();
    public static void Trace(string source, string name, string message) => Write(RewindLevel.Trace, source, name, message);
    public static void Debug(string source, string name, string message) => Write(RewindLevel.Debug, source, name, message);
    public static void Information(string source, string name, string message) => Write(RewindLevel.Information, source, name, message);
    public static void Warning(string source, string name, string message) => Write(RewindLevel.Warning, source, name, message);
    public static void Error(string source, string name, string message) => Write(RewindLevel.Error, source, name, message);
    public static void Critical(string source, string name, string message) => Write(RewindLevel.Critical, source, name, message);
    public static void TriggerIncident(string name, string details) => _state?.Trigger(name, details);
    public static RewindHealthSnapshot GetHealthSnapshot()
        => _state?.GetHealth() ?? new RewindHealthSnapshot(0, 0, 0, 0, 0, 0);

    public static Task<FlushResult> FlushAsync(TimeSpan timeout)
        => _state?.FlushAsync(timeout) ?? Task.FromResult(new FlushResult(true, 0));

    public static async Task<ShutdownResult> ShutdownAsync(TimeSpan timeout)
    {
        IRewindRecorder? state;
        lock (Gate)
        {
            state = _state;
            _state = null;
        }

        if (state == null)
        {
            return new ShutdownResult(true, 0);
        }

        FlushResult flush = await state.FlushAsync(timeout).ConfigureAwait(false);
        state.Dispose();
        return new ShutdownResult(flush.Completed, flush.UnresolvedCount);
    }

    public static void Shutdown()
    {
        IRewindRecorder? state;
        lock (Gate)
        {
            state = _state;
            _state = null;
        }

        state?.Dispose();
    }

    private static void Write(RewindLevel level, string source, string name, string message)
        => _state?.Write(level, source, name, message);

    private sealed class RecorderState : IRewindRecorder
    {
        private readonly RewindOptions _options;
        private readonly BoundedQueue<OutboundFrame> _events;
        private readonly BoundedQueue<OutboundFrame> _controls;
        private readonly AutoResetEvent _available = new AutoResetEvent(false);
        private readonly Thread _sender;
        private readonly Guid _clientId = Guid.NewGuid();
        private readonly object _contextGate = new object();
        private Dictionary<string, string> _context = new Dictionary<string, string>(StringComparer.Ordinal);
        private volatile bool _stopping;
        private long _sequence;
        private long _accepted;
        private long _sent;
        private long _droppedQueueFull;
        private long _droppedInvalid;
        private long _transportFailures;
        private long _pending;

        public RecorderState(RewindOptions options)
        {
            if (options.EventQueueCapacity <= 0 || options.ControlQueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Queue capacities must be positive.");
            }
            if (options.MaximumContextEntries <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Context capacity must be positive.");
            }

            _options = options;
            _events = new BoundedQueue<OutboundFrame>(options.EventQueueCapacity);
            _controls = new BoundedQueue<OutboundFrame>(options.ControlQueueCapacity);
            _sender = new Thread(SenderLoop) { IsBackground = true, Name = "Rewind sender" };
            _sender.Start();
        }

        public void Write(RewindLevel level, string source, string name, string message)
        {
            if (!Valid(source) || !Valid(name) || !Valid(message))
            {
                Interlocked.Increment(ref _droppedInvalid);
                return;
            }

            long sequence = Interlocked.Increment(ref _sequence);
            Dictionary<string, string> context;
            lock (_contextGate)
            {
                context = new Dictionary<string, string>(_context, StringComparer.Ordinal);
            }

            if (!FitsFrameBudget(source, name, message, context))
            {
                Interlocked.Increment(ref _droppedInvalid);
                return;
            }

            var value = new RewindEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                sequence,
                level,
                source,
                name,
                message,
                context,
                Process.GetCurrentProcess().Id,
                Environment.CurrentManagedThreadId);
            byte[] frame = WireJson.SerializeEvent(Envelope(WireMessageType.Event, sequence), value);
            Admit(_events, new OutboundFrame(sequence, frame));
        }

        public void Trigger(string name, string details)
        {
            if (!Valid(name) || !Valid(details))
            {
                Interlocked.Increment(ref _droppedInvalid);
                return;
            }

            long sequence = Interlocked.Increment(ref _sequence);
            Admit(
                _controls,
                new OutboundFrame(
                    sequence,
                    WireJson.SerializeTrigger(Envelope(WireMessageType.Trigger, sequence), name, details)));
        }

        public void SetContext(string key, string value)
        {
            if (!Valid(key) || !Valid(value))
            {
                Interlocked.Increment(ref _droppedInvalid);
                return;
            }

            lock (_contextGate)
            {
                if (!_context.ContainsKey(key) && _context.Count >= _options.MaximumContextEntries)
                {
                    Interlocked.Increment(ref _droppedInvalid);
                    return;
                }

                var replacement = new Dictionary<string, string>(_context, StringComparer.Ordinal) { [key] = value };
                _context = replacement;
            }
        }

        public bool RemoveContext(string key)
        {
            lock (_contextGate)
            {
                if (!_context.ContainsKey(key))
                {
                    return false;
                }

                var replacement = new Dictionary<string, string>(_context, StringComparer.Ordinal);
                bool removed = replacement.Remove(key);
                _context = replacement;
                return removed;
            }
        }

        public void ClearContext()
        {
            lock (_contextGate)
            {
                _context = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        public RewindHealthSnapshot GetHealth() => new RewindHealthSnapshot(
            Interlocked.Read(ref _accepted),
            Interlocked.Read(ref _sent),
            Interlocked.Read(ref _droppedQueueFull),
            Interlocked.Read(ref _droppedInvalid),
            Interlocked.Read(ref _transportFailures),
            Interlocked.Read(ref _pending));

        public async Task<FlushResult> FlushAsync(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (Interlocked.Read(ref _pending) > 0 && DateTimeOffset.UtcNow < deadline)
            {
                _available.Set();
                await Task.Delay(10).ConfigureAwait(false);
            }

            long unresolved = Interlocked.Read(ref _pending);
            return new FlushResult(unresolved == 0, unresolved);
        }

        public void Dispose()
        {
            _stopping = true;
            _available.Set();
            _sender.Join(TimeSpan.FromSeconds(2));
            _available.Dispose();
        }

        private static bool Valid(string? value)
            => value != null && value.Length <= ProtocolConstants.MaximumFieldCharacters;

        private static bool FitsFrameBudget(
            string source,
            string name,
            string message,
            IReadOnlyDictionary<string, string> context)
        {
            int bytes = 4096;
            bytes += Encoding.UTF8.GetByteCount(source);
            bytes += Encoding.UTF8.GetByteCount(name);
            bytes += Encoding.UTF8.GetByteCount(message);
            foreach (KeyValuePair<string, string> item in context)
            {
                bytes += Encoding.UTF8.GetByteCount(item.Key);
                bytes += Encoding.UTF8.GetByteCount(item.Value);
                bytes += 8;
                if (bytes > ProtocolConstants.MaximumFrameBytes)
                {
                    return false;
                }
            }

            return bytes <= ProtocolConstants.MaximumFrameBytes;
        }

        private WireMessage Envelope(WireMessageType type, long sequence) => new WireMessage
        {
            Type = type,
            ClientInstanceId = _clientId,
            MessageId = Guid.NewGuid(),
            ClientSequence = sequence,
        };

        private void Admit(BoundedQueue<OutboundFrame> queue, OutboundFrame frame)
        {
            if (frame.Payload.Length > ProtocolConstants.MaximumFrameBytes || !queue.TryEnqueue(frame))
            {
                Interlocked.Increment(ref _droppedQueueFull);
                return;
            }

            Interlocked.Increment(ref _accepted);
            Interlocked.Increment(ref _pending);
            _available.Set();
        }

        private void SenderLoop()
        {
            while (!_stopping)
            {
                using (var pipe = new NamedPipeClientStream(".", _options.AgentPipeName, PipeDirection.Out, PipeOptions.None))
                {
                    try
                    {
                        pipe.Connect(_options.ConnectTimeoutMilliseconds);
                        Send(pipe, WireJson.SerializeHello(Envelope(WireMessageType.Hello, 0)), countAsSent: false);
                        while (!_stopping && pipe.IsConnected)
                        {
                            if (TryDequeueNext(out OutboundFrame? next) && next != null)
                            {
                                try
                                {
                                    Send(pipe, next.Payload, countAsSent: true);
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref _pending);
                                }
                            }
                            else
                            {
                                _available.WaitOne(100);
                            }
                        }
                    }
                    catch (IOException)
                    {
                        Interlocked.Increment(ref _transportFailures);
                    }
                    catch (TimeoutException)
                    {
                        Interlocked.Increment(ref _transportFailures);
                    }
                }

                if (!_stopping)
                {
                    _available.WaitOne(250);
                }
            }
        }

        private void Send(Stream stream, byte[] payload, bool countAsSent)
        {
            byte[] length = BitConverter.GetBytes(payload.Length);
            stream.Write(length, 0, length.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
            if (countAsSent)
            {
                Interlocked.Increment(ref _sent);
            }
        }

        private bool TryDequeueNext(out OutboundFrame? value)
        {
            bool hasControl = _controls.TryPeek(out OutboundFrame? control);
            bool hasEvent = _events.TryPeek(out OutboundFrame? item);
            if (!hasControl && !hasEvent)
            {
                value = null;
                return false;
            }

            return hasControl && (!hasEvent || control!.Sequence < item!.Sequence)
                ? _controls.TryDequeue(out value)
                : _events.TryDequeue(out value);
        }

        private sealed class OutboundFrame
        {
            public OutboundFrame(long sequence, byte[] payload)
            {
                Sequence = sequence;
                Payload = payload;
            }

            public long Sequence { get; }
            public byte[] Payload { get; }
        }
    }
}
