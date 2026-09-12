using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct RawSeries(int[] Seconds, float[] Values);
public readonly record struct MinuteSeries(int[] Minute, float[] Min, float[] Max, float[] Avg);
public sealed class HistoryStore(DateTimeOffset epoch, int rawCapacity = 900, int minuteCapacity = 2880)
{
    /// <summary>
    /// One sensor's history. The raw ring is allocated eagerly (every sensor gets a sample on the
    /// first tick). The minute tier is allocated only on that sensor's first minute rollover: the
    /// in-progress bucket lives in the _cur* scalars, so a 609-sensor box that has been running for
    /// under a minute pays for the raw rings alone (~4.2 MB) instead of ~36 MB.
    /// The tier still holds <c>minuteCapacity</c> buckets in total: <c>minuteCapacity - 1</c>
    /// completed ones in the ring plus the in-progress one.
    /// </summary>
    private sealed class Series
    {
        public readonly int[] Sec; public readonly float[] Val; public int RawHead, RawCount;
        private readonly int _completedCap;
        private int[]? _min; private float[]? _mMin, _mMax, _mSum; private ushort[]? _mCount;
        private int _head, _count;
        private int _curMinute = int.MinValue; private float _curMin, _curMax, _curSum; private ushort _curCount;
        public Series(int rawCap, int minCap) { Sec = new int[rawCap]; Val = new float[rawCap]; _completedCap = Math.Max(0, minCap - 1); }
        public bool MinuteTierAllocated => _min is not null;
        public int CompletedMinuteCapacity => _completedCap;
        public void AddRaw(int sec, float v)
        {
            int idx = (RawHead + RawCount) % Sec.Length;
            if (RawCount == Sec.Length) { RawHead = (RawHead + 1) % Sec.Length; idx = (RawHead + RawCount - 1) % Sec.Length; } else RawCount++;
            Sec[idx] = sec; Val[idx] = v;
        }
        public void AddMinute(int minute, float v)
        {
            if (_curMinute == int.MinValue) StartBucket(minute);
            else if (minute != _curMinute) { FlushBucket(); StartBucket(minute); }
            if (float.IsNaN(v)) return;
            _curMin = Math.Min(_curMin, v); _curMax = Math.Max(_curMax, v); _curSum += v; _curCount++;
        }
        private void StartBucket(int minute) { _curMinute = minute; _curMin = float.MaxValue; _curMax = float.MinValue; _curSum = 0; _curCount = 0; }
        private void FlushBucket()
        {
            if (_completedCap == 0) return;
            _min ??= new int[_completedCap]; _mMin ??= new float[_completedCap]; _mMax ??= new float[_completedCap]; _mSum ??= new float[_completedCap]; _mCount ??= new ushort[_completedCap];
            int idx;
            if (_count == _completedCap) { idx = _head; _head = (_head + 1) % _completedCap; }
            else { idx = (_head + _count) % _completedCap; _count++; }
            _min[idx] = _curMinute; _mMin[idx] = _curMin; _mMax[idx] = _curMax; _mSum[idx] = _curSum; _mCount[idx] = _curCount;
        }
        public MinuteSeries Read()
        {
            int n = _count + (_curMinute == int.MinValue ? 0 : 1);
            var m = new int[n]; var mn = new float[n]; var mx = new float[n]; var av = new float[n];
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) % _completedCap; m[i] = _min![idx];
                bool empty = _mCount![idx] == 0;
                mn[i] = empty ? float.NaN : _mMin![idx]; mx[i] = empty ? float.NaN : _mMax![idx]; av[i] = empty ? float.NaN : _mSum![idx] / _mCount[idx];
            }
            if (_curMinute != int.MinValue)
            {
                bool empty = _curCount == 0; m[n - 1] = _curMinute;
                mn[n - 1] = empty ? float.NaN : _curMin; mx[n - 1] = empty ? float.NaN : _curMax; av[n - 1] = empty ? float.NaN : _curSum / _curCount;
            }
            return new MinuteSeries(m, mn, mx, av);
        }
        public const int BytesPerRawSlot = sizeof(int) + sizeof(float), BytesPerMinuteSlot = sizeof(int) + 3 * sizeof(float) + sizeof(ushort);
    }
    private readonly Dictionary<SensorId, Series> _series = []; private readonly object _lock = new();
    public DateTimeOffset Epoch { get; } = epoch; public int RawCapacity { get; } = rawCapacity; public int MinuteCapacity { get; } = minuteCapacity;
    public int SecondsSinceEpoch(DateTimeOffset t) => (int)Math.Round((t - Epoch).TotalSeconds);
    public void Append(SensorSnapshot snapshot)
    {
        int sec = SecondsSinceEpoch(snapshot.Timestamp);
        lock (_lock)
            foreach (var r in snapshot.Readings)
            {
                if (!_series.TryGetValue(r.Id, out var s)) _series[r.Id] = s = new Series(RawCapacity, MinuteCapacity);
                float v = r.Quality == DataQuality.Ok && r.Value is { } d ? (float)d : float.NaN;
                s.AddRaw(sec, v); s.AddMinute(sec / 60, v);
            }
    }
    public RawSeries GetRaw(SensorId id)
    {
        lock (_lock)
        {
            if (!_series.TryGetValue(id, out var s)) return new RawSeries([], []);
            var sec = new int[s.RawCount]; var val = new float[s.RawCount];
            for (int i = 0; i < s.RawCount; i++) { int idx = (s.RawHead + i) % s.Sec.Length; sec[i] = s.Sec[idx]; val[i] = s.Val[idx]; }
            return new RawSeries(sec, val);
        }
    }
    public MinuteSeries GetMinutes(SensorId id)
    {
        lock (_lock) return _series.TryGetValue(id, out var s) ? s.Read() : new MinuteSeries([], [], [], []);
    }
    /// <summary>Bytes actually allocated: every sensor's raw ring, plus the minute tier only for
    /// sensors that have already rolled over a minute boundary.</summary>
    public long EstimatedBytes
    {
        get
        {
            lock (_lock)
            {
                long total = 0;
                foreach (var s in _series.Values)
                    total += RawCapacity * Series.BytesPerRawSlot + (s.MinuteTierAllocated ? (long)s.CompletedMinuteCapacity * Series.BytesPerMinuteSlot : 0);
                return total;
            }
        }
    }
}
