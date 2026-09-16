using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Alerts;

/// <summary>
/// Turns a stream of <see cref="Snapshot"/>s into alert events. Debounces threats, readability
/// and header member count independently, so a single blinking criminal/suspect/war-target flag
/// (which can flip a row between Enemy, NoIcon and Unknown frame to frame) doesn't itself cause
/// repeated alerts. Rate limiting per channel belongs to the M2 dispatcher, not here.
/// </summary>
public sealed class AlertState
{
    private readonly AlertStateOptions _options;
    private readonly StableTracker<int> _threats;
    private readonly StableTracker<Readability> _readability;
    private readonly StableTracker<int?> _memberCount;

    public AlertState(AlertStateOptions options)
    {
        _options = options;
        _threats = new StableTracker<int>(0, options.StableFrames);
        _readability = new StableTracker<Readability>(Readability.Full, options.StableFrames);
        _memberCount = new StableTracker<int?>(null, options.StableFrames);
    }

    public IReadOnlyList<AlertEvent> Update(Snapshot snapshot, DateTimeOffset now)
    {
        var events = new List<AlertEvent>();

        if (_threats.Update(snapshot.Threats.Total, out int previousThreats))
        {
            if (snapshot.Threats.Total > previousThreats)
            {
                events.Add(new ThreatIncreased(previousThreats, snapshot.Threats.Total, snapshot.Threats, now));
            }
            else if (snapshot.Threats.Total == 0 && previousThreats > 0 && _options.EmitLocalClear)
            {
                events.Add(new LocalClear(now));
            }
        }

        if (_readability.Update(snapshot.Readability, out Readability previousReadability))
        {
            events.Add(new ReadabilityChanged(previousReadability, snapshot.Readability, snapshot.HeaderMemberCount, snapshot.OccupiedFullRows, now));
        }

        // A null reading (header digits unreadable this frame) never touches the tracker —
        // it neither resets progress toward the current pending value nor counts toward it.
        if (snapshot.HeaderMemberCount is { } rawCount
            && _memberCount.Update(rawCount, out int? previousCount)
            && previousCount is { } previous
            && rawCount > previous
            && _readability.Stable != Readability.Full
            && _options.AlertOnMemberIncreaseWhileUnreadable)
        {
            events.Add(new MemberCountIncreasedWhileUnreadable(previous, rawCount, now));
        }

        return events;
    }

    /// <summary>Tracks a raw value stream and reports the debounced "stable" value: a new
    /// reading only takes effect once it has repeated <c>stableFrames</c> times in a row.</summary>
    private sealed class StableTracker<T>
    {
        private readonly int _stableFrames;
        private T _pendingValue;
        private int _pendingCount;

        public StableTracker(T initialStable, int stableFrames)
        {
            Stable = initialStable;
            _stableFrames = stableFrames;
            _pendingValue = initialStable;
            _pendingCount = stableFrames; // the initial value counts as already stable
        }

        public T Stable { get; private set; }

        /// <summary>Feeds one new raw reading. Returns true (and sets <paramref name="previous"/>)
        /// only on the update where Stable actually changes.</summary>
        public bool Update(T raw, out T previous)
        {
            previous = Stable;

            if (EqualityComparer<T>.Default.Equals(raw, _pendingValue))
            {
                _pendingCount++;
            }
            else
            {
                _pendingValue = raw;
                _pendingCount = 1;
            }

            if (_pendingCount >= _stableFrames && !EqualityComparer<T>.Default.Equals(_pendingValue, Stable))
            {
                Stable = _pendingValue;
                return true;
            }

            return false;
        }
    }
}
