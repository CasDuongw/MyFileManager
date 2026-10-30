namespace FM.App.Tests.Fakes;

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<TestTimer> _timers = new();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, callback, state, dueTime);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        _utcNow += delta;
        foreach (var timer in _timers.ToList())
        {
            timer.CheckTrigger(_utcNow);
        }
    }

    private sealed class TestTimer : ITimer
    {
        private readonly TestTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset _dueAt;
        private bool _disposed;
        private bool _fired;

        public TestTimer(TestTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            _dueAt = provider._utcNow + dueTime;
        }

        public void CheckTrigger(DateTimeOffset now)
        {
            if (!_disposed && !_fired && now >= _dueAt)
            {
                _fired = true;
                _callback(_state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = _provider._utcNow + dueTime;
            _fired = false;
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            _provider._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

