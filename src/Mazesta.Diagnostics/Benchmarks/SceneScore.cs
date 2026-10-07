using System.Globalization;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>
/// The points of the 3-D scene benchmark: one number each for the graphics card, the processor and the RAM, and one for the machine as a whole, so
/// two systems can be set side by side and the part that holds one back is seen. Built the way 3DMark builds its scores (a measured rate times a
/// fixed constant, the parts joined by a weighted harmonic mean so a weak part is not hidden by a strong one), on Mazesta's own scale: the
/// constants are chosen so that a mid-range machine lands in the thousands, and they are never changed without raising the benchmark's version, so
/// the numbers of one version are comparable with each other and with nothing else.
/// <list type="bullet">
/// <item><b>Graphics</b> = 100 × the frame rate the card alone could hold: <c>fps × 100</c>. Not scaled by the pixel count: a bigger picture must not raise the score (it
/// can only lower the rate), so the resolution is a setting of the run - part of the record's key, lists never mix resolutions - not a factor in the number.
/// The frame's time on the card is its submission to its completion; the processor's share of the frame is the CPU score's.</item>
/// <item><b>CPU</b> = 3 × the frames a second the processor and driver can prepare (record and submit) for this scene: its single-thread speed as the
/// game loop of a real program feels it.</item>
/// <item><b>RAM</b> = 5000 × √((STREAM Triad GB/s ÷ 40) × (80 ns ÷ random-access latency)): bandwidth and latency count equally, so neither one alone decides it.</item>
/// <item><b>Overall</b> = weighted harmonic mean, weights 0.75 graphics, 0.15 CPU, 0.10 RAM (3DMark Time Spy weighs graphics 0.85 and CPU 0.15).</item>
/// </list>
/// </summary>
public static class SceneScore
{
    public const double GraphicsWeight = 0.75, CpuWeight = 0.15, RamWeight = 0.10;

    public static double Graphics(double gpuFramesPerSecond) => gpuFramesPerSecond * 100;
    public static double Cpu(double preparedFramesPerSecond) => preparedFramesPerSecond * 3;
    public static double Ram(double triadGbPerSecond, double latencyNs) => 5000 * Math.Sqrt(triadGbPerSecond / 40.0 * (80.0 / latencyNs));
    public static double Overall(double graphics, double cpu, double ram)
        => (GraphicsWeight + CpuWeight + RamWeight) / (GraphicsWeight / graphics + CpuWeight / cpu + RamWeight / ram);

    /// <summary>How the cost of a frame (each part's weight over its score: the terms of the harmonic mean) divides between the three parts, in percent: the
    /// model's reading of where the time goes, so a result shows how much a faster card, processor or RAM could still change. Not a measurement of the part.</summary>
    public static (double Graphics, double Cpu, double Ram) Shares(double graphics, double cpu, double ram)
    {
        double g = GraphicsWeight / graphics, c = CpuWeight / cpu, r = RamWeight / ram, sum = g + c + r;
        return (g / sum * 100, c / sum * 100, r / sum * 100);
    }
    /// <summary>What the overall score would gain, in percent, if the RAM were a tenth faster (its score a tenth higher): how much the RAM's speed and latency matter to this machine's result, in the score's own model.</summary>
    public static double RamEffect(double graphics, double cpu, double ram) => (Overall(graphics, cpu, ram * 1.1) / Overall(graphics, cpu, ram) - 1) * 100;

    /// <summary>Which of the two sides of a frame holds the frame rate back: the one that takes longer. With both within a tenth of each other neither
    /// is named (a balanced scene), and the result says so.</summary>
    public enum Limit { Balanced, Graphics, Cpu }
    public static Limit Bottleneck(double gpuSeconds, double cpuSeconds)
        => Math.Abs(gpuSeconds - cpuSeconds) <= 0.1 * Math.Max(gpuSeconds, cpuSeconds) ? Limit.Balanced : gpuSeconds > cpuSeconds ? Limit.Graphics : Limit.Cpu;

    public static string Name(Limit limit) => limit.ToString();
    public static string Points(double score) => Math.Round(score).ToString("F0", CultureInfo.InvariantCulture);
}
