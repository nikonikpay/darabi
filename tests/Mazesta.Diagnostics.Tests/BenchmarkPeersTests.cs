using Xunit; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Tests;

public class BenchmarkPeersTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-peers-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private static BenchmarkRun Run(string part, string system, double value, string bench = "bench.cpu.multi", string settings = "", int minutes = 0)
        => new(Guid.NewGuid().ToString("N"), T0.AddMinutes(minutes), bench, 1, settings, part, system, "PC", "PC · CPU", value, "GFLOPS");

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", "Intel Core i7-8700")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Ryzen 7 7800X3D")]
    [InlineData("  NVIDIA GeForce   RTX 3080 ", "NVIDIA GeForce RTX 3080")]
    [InlineData("Samsung SSD 980 PRO 1TB", "Samsung SSD 980 PRO 1TB")]
    public void Part_names_lose_marks_and_clock_tails_only(string raw, string name) => Assert.Equal(name, BenchmarkPeers.PartName(raw));

    [Fact] public void Settings_leave_out_the_device_and_keep_a_fixed_order()
        => Assert.Equal("fileMb=1024|res=1080", BenchmarkPeers.Settings(new Dictionary<string, string> { ["res"] = "1080", ["drive"] = @"C:\", ["fileMb"] = "1024", ["gpu"] = "x|1" }));
    [Fact] public void A_list_without_settings_is_named_by_its_benchmark_and_version()
    {
        Assert.Equal("bench.cpu.multi-v1.json", BenchmarkPeers.FileName(BenchmarkPeers.TableKey("bench.cpu.multi", 1, "")));
        Assert.Matches(@"^bench\.storage-v1-[0-9a-f]{10}\.json$", BenchmarkPeers.FileName(BenchmarkPeers.TableKey("bench.storage", 1, "fileMb=1024")));
    }

    [Fact] public void Each_system_counts_once_with_its_best_run_and_the_entry_is_their_median()
    {
        var t = Assert.Single(BenchmarkPeers.Aggregate([Run("CPU A", "s1", 100), Run("CPU A", "s1", 140), Run("CPU A", "s2", 120), Run("CPU A", "s3", 90)], T0));
        var e = Assert.Single(t.Entries);
        Assert.Equal(120, e.Median); Assert.Equal(140, e.Best); Assert.Equal(3, e.Systems); Assert.Equal(4, e.Runs);
    }
    [Fact] public void Spellings_of_one_model_are_one_entry_and_lists_are_best_first()
    {
        var t = Assert.Single(BenchmarkPeers.Aggregate([Run("Intel(R) Core(TM) i5-12400", "s1", 80), Run("Intel Core i5-12400", "s2", 100), Run("AMD Ryzen 5 5600", "s3", 200)], T0));
        Assert.Equal(["AMD Ryzen 5 5600", "Intel Core i5-12400"], t.Entries.Select(e => e.Part));
        Assert.Equal(90, t.Entries[1].Median);
    }
    [Fact] public void Other_settings_make_other_lists()
        => Assert.Equal(2, BenchmarkPeers.Aggregate([Run("D", "s1", 1, "bench.storage", "fileMb=1024"), Run("D", "s1", 1, "bench.storage", "fileMb=4096")], T0).Count);

    [Fact] public void The_ranking_says_how_far_this_result_is_from_each_model()
    {
        var table = Assert.Single(BenchmarkPeers.Aggregate([Run("Fast", "s1", 200), Run("Slow", "s2", 50)], T0));
        var k = BenchmarkPeers.Rank(table, [], 100, "Slow", higherIsBetter: true);
        Assert.Equal(1, k.MineIndex); Assert.Equal(1, k.Beaten);
        Assert.Equal(-50, k.Rows[0].DiffPercent, 6); Assert.Equal(100, k.Rows[1].DiffPercent, 6);
        Assert.True(k.Rows[1].Same); Assert.False(k.Rows[0].Same);
    }
    [Fact] public void Lower_is_better_turns_the_sign() => Assert.Equal(50, BenchmarkPeers.Diff(5, 10, higherIsBetter: false), 6);
    [Fact] public void Local_models_join_the_list_but_a_published_model_is_not_counted_twice()
    {
        var table = Assert.Single(BenchmarkPeers.Aggregate([Run("A", "s1", 100)], T0));
        var local = BenchmarkPeers.Aggregate([Run("A", "s9", 999), Run("B", "s8", 50)], T0)[0].Entries;
        var k = BenchmarkPeers.Rank(table, local, 75, null, true);
        Assert.Equal(2, k.Total);
        Assert.Equal(100, k.Rows.Single(r => r.Entry.Part == "A").Entry.Median);
        Assert.True(k.Rows.Single(r => r.Entry.Part == "B").Local);
    }
    [Fact] public void No_result_yet_ranks_nothing()
    {
        var k = BenchmarkPeers.Rank(BenchmarkPeers.Aggregate([Run("A", "s1", 100)], T0)[0], [], double.NaN, null, true);
        Assert.Equal(0, k.Beaten); Assert.Equal(0, k.MineIndex); Assert.True(double.IsNaN(k.Rows[0].DiffPercent));
    }

    [Fact] public void The_run_log_keeps_runs_across_restarts_and_skips_damaged_lines()
    {
        var log = new BenchmarkRunLog(_dir);
        log.Append(Run("A", "s1", 100)); log.Append(Run("B", "s2", 80));
        File.AppendAllText(Path.Combine(log.Folder, "2026-09.jsonl"), "{not json\n");
        var again = new BenchmarkRunLog(_dir);
        Assert.Equal(2, again.Of("bench.cpu.multi@1").Count);
        Assert.Equal(2, again.Entries("bench.cpu.multi@1").Count);
    }
    [Fact] public void A_list_file_round_trips_and_one_named_for_another_list_is_refused()
    {
        var t = Assert.Single(BenchmarkPeers.Aggregate([Run("A", "s1", 100)], T0));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, BenchmarkPeers.FileName(t.Key)), BenchmarkPeers.Write(t));
        Assert.Equal(100, new PeerDatabase(_dir).Table(t.Key)!.Entries[0].Median);
        File.WriteAllText(Path.Combine(_dir, BenchmarkPeers.FileName("bench.cpu.single@1")), BenchmarkPeers.Write(t));
        Assert.Null(new PeerDatabase(_dir).Table("bench.cpu.single@1"));
    }

    [Theory]
    [InlineData(6, 26, true, "4.3×")]
    [InlineData(26, 6, false, "4.3×")]
    [InlineData(80, 100, true, "25%")]
    [InlineData(100, 95.5, false, "4.7%")]
    [InlineData(10, 19.9, true, "99%")]
    [InlineData(10, 120, true, "12×")]
    [InlineData(100, 100.2, true, "≈")]
    public void The_gap_is_said_from_the_faster_side_as_a_percentage_up_to_double_then_a_multiple(double mine, double theirs, bool theyLead, string text)
    {
        var g = BenchmarkPeers.Gap(mine, theirs, higherIsBetter: true)!.Value;
        Assert.Equal(theyLead, g.TheyLead); Assert.Equal(text, g.Text);
    }
    [Fact] public void A_lower_is_better_gap_turns_around() => Assert.False(BenchmarkPeers.Gap(5, 10, higherIsBetter: false)!.Value.TheyLead);
    [Fact] public void No_result_has_no_gap() => Assert.Null(BenchmarkPeers.Gap(double.NaN, 10, true));

    [Fact] public void An_overclocked_part_is_an_entry_of_its_own_and_a_later_mark_can_say_so()
    {
        var a = Run("CPU A", "s1", 100); var b = Run("CPU A", "s2", 140) with { Overclocked = true }; var c = Run("CPU A", "s3", 150);
        var marks = new Dictionary<string, BenchmarkMark> { [c.Id] = new(false, true, null, T0) };
        var t = Assert.Single(BenchmarkPeers.Aggregate([a, b, c], T0, marks));
        Assert.Equal(2, t.Entries.Count);
        Assert.Equal(145, t.Entries.Single(e => e.Overclocked).Median);
        Assert.Equal(100, t.Entries.Single(e => !e.Overclocked).Median);
    }
    [Fact] public void Featured_runs_are_listed_one_by_one_and_the_entry_keeps_the_run_nearest_its_median()
    {
        var spec = new SpecItem(BenchmarkDetails.PartGroup, "Spec_Cores", "8");
        var a = Run("CPU A", "s1", 100) with { Details = [spec] }; var b = Run("CPU A", "s2", 120); var c = Run("CPU A", "s3", 170);
        var t = Assert.Single(BenchmarkPeers.Aggregate([a, b, c], T0, new Dictionary<string, BenchmarkMark> { [a.Id] = new(true, null, "stock cooler", T0) }));
        var f = Assert.Single(t.Featured!);
        Assert.Equal("stock cooler", f.Note); Assert.Equal("8", f.Details![0].Value);
        Assert.Equal(120, t.Entries[0].Sample!.Value);
    }
    [Fact] public void The_latest_mark_of_a_run_wins_when_copies_are_gathered()
    {
        var on = new Dictionary<string, BenchmarkMark> { ["r"] = new(true, null, null, T0) };
        var off = new Dictionary<string, BenchmarkMark> { ["r"] = new(false, null, null, T0.AddHours(1)) };
        Assert.False(BenchmarkMarks.Merge([off, on])["r"].Featured);
    }
    [Fact] public void Marks_are_kept_across_restarts_and_change_the_logged_runs()
    {
        var log = new BenchmarkRunLog(_dir); var r = Run("A", "s1", 100); log.Append(r);
        log.Marks.Set(r.Id, new(true, true, null, T0));
        var again = new BenchmarkRunLog(_dir);
        Assert.True(again.Of("bench.cpu.multi@1")[0].Overclocked);
        Assert.Single(again.Table("bench.cpu.multi@1")!.Featured!);
    }
    [Fact] public void A_hybrid_cpu_counts_its_performance_and_efficient_cores_apart()
    {
        var cores = new List<Mazesta.Diagnostics.Cpu.CpuCore> { new(0, 0, 0b11, 1), new(1, 0, 0b1100, 1), new(2, 0, 0b10000, 0), new(3, 0, 0b100000, 0), new(4, 0, 0b1000000, 0) };
        Assert.Equal((2, 3, 7), BenchmarkDetails.Split(cores));
        Assert.Equal((2, 0, 2), BenchmarkDetails.Split([new(0, 0, 1, 0), new(1, 0, 2, 0)]));
    }

    [Fact] public void Records_saved_before_the_hardware_was_known_move_under_the_full_name()
    {
        var r = new BenchmarkRecords(_dir);
        r.Offer("PC | ", "PC", "bench.cpu.multi", new BenchmarkResult(CpuBenchmark.Multi.Id, BenchmarkStatus.Completed, T0, T0, [new("Bench_Cpu_Gflops", 100, "GFLOPS")], null));
        r.AdoptUnnamed("PC | CPU | GPU", "PC");
        Assert.Equal(100, new BenchmarkRecords(_dir).Best("PC | CPU | GPU", "bench.cpu.multi")!.Value);
        Assert.Null(r.Best("PC | ", "bench.cpu.multi"));
    }
}
