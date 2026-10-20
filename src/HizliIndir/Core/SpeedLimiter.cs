namespace HizliIndir.Core;

/// <summary>Basit jeton kovası. Saniyedeki bayt sınırı 0 ise hiç beklemez.</summary>
public sealed class SpeedLimiter
{
    readonly object _gate = new();
    long _bytesPerSecond;
    double _tokens;
    long _lastTicks = Environment.TickCount64;

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set { Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value)); lock (_gate) _tokens = 0; }
    }

    public async ValueTask WaitAsync(int bytes, CancellationToken ct)
    {
        while (true)
        {
            var rate = BytesPerSecond;
            if (rate <= 0) return;
            int delay;
            lock (_gate)
            {
                var now = Environment.TickCount64;
                _tokens = Math.Min(rate, _tokens + (now - _lastTicks) * rate / 1000.0);
                _lastTicks = now;
                if (_tokens >= bytes || _tokens >= rate) { _tokens -= bytes; return; }
                delay = (int)Math.Ceiling((bytes - _tokens) * 1000.0 / rate);
            }
            await Task.Delay(Math.Clamp(delay, 5, 500), ct);
        }
    }
}

/// <summary>"N saatte X MB'tan fazla indirme" kuralını izler.</summary>
public sealed class UsageWindow
{
    readonly object _gate = new();
    readonly Queue<(DateTime at, long bytes)> _samples = new();
    long _pending;
    DateTime _lastFlush = DateTime.Now;

    public void Add(long bytes)
    {
        lock (_gate)
        {
            _pending += bytes;
            if ((DateTime.Now - _lastFlush).TotalSeconds >= 5) Flush();
        }
    }

    void Flush()
    {
        if (_pending > 0) _samples.Enqueue((DateTime.Now, _pending));
        _pending = 0; _lastFlush = DateTime.Now;
    }

    public long BytesInLast(TimeSpan span)
    {
        lock (_gate)
        {
            Flush();
            var cutoff = DateTime.Now - span;
            while (_samples.Count > 0 && _samples.Peek().at < cutoff) _samples.Dequeue();
            return _samples.Sum(s => s.bytes);
        }
    }
}
