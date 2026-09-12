using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct RawSeries(int[] Seconds, float[] Values);
public readonly record struct MinuteSeries(int[] Minute, float[] Min, float[] Max, float[] Avg);
public sealed class HistoryStore(DateTimeOffset epoch, int rawCapacity = 900, int minuteCapacity = 2880)
{
    private sealed class Series(int rawCap, int minCap)
    {
        public readonly int[] Sec = new int[rawCap]; public readonly float[] Val = new float[rawCap]; public int RawHead, RawCount;
        public readonly int[] Min = new int[minCap]; public readonly float[] MMin = new float[minCap], MMax = new float[minCap], MSum = new float[minCap]; public readonly ushort[] MCount = new ushort[minCap]; public int MinHead, MinCount;
        public void AddRaw(int sec, float v)
        {
            int idx = (RawHead + RawCount) % Sec.Length;
            if (RawCount == Sec.Length) { RawHead = (RawHead + 1) % Sec.Length; idx = (RawHead + RawCount - 1) % Sec.Length; } else RawCount++;
            Sec[idx] = sec; Val[idx] = v;
        }
        public void AddMinute(int minute, float v)
        {
            int last = MinCount == 0 ? -1 : (MinHead + MinCount - 1) % Min.Length;
            if (last < 0 || Min[last] != minute)
            {
                if (MinCount == Min.Length) { MinHead = (MinHead + 1) % Min.Length; last = (MinHead + MinCount - 1) % Min.Length; } else { last = (MinHead + MinCount) % Min.Length; MinCount++; }
                Min[last] = minute; MMin[last] = float.MaxValue; MMax[last] = float.MinValue; MSum[last] = 0; MCount[last] = 0;
            }
            if (float.IsNaN(v)) return;
            MMin[last] = Math.Min(MMin[last], v); MMax[last] = Math.Max(MMax[last], v); MSum[last] += v; MCount[last]++;
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
        lock (_lock)
        {
            if (!_series.TryGetValue(id, out var s)) return new MinuteSeries([], [], [], []);
            int n = s.MinCount; var m = new int[n]; var mn = new float[n]; var mx = new float[n]; var av = new float[n];
            for (int i = 0; i < n; i++)
            {
                int idx = (s.MinHead + i) % s.Min.Length; m[i] = s.Min[idx];
                bool empty = s.MCount[idx] == 0; mn[i] = empty ? float.NaN : s.MMin[idx]; mx[i] = empty ? float.NaN : s.MMax[idx]; av[i] = empty ? float.NaN : s.MSum[idx] / s.MCount[idx];
            }
            return new MinuteSeries(m, mn, mx, av);
        }
    }
    public long EstimatedBytes { get { lock (_lock) return (long)_series.Count * (RawCapacity * Series.BytesPerRawSlot + MinuteCapacity * Series.BytesPerMinuteSlot); } }
}
