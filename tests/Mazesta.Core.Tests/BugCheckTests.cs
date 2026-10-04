using System.Buffers.Binary; using Xunit; using Mazesta.Core.Crashes;
namespace Mazesta.Core.Tests;

/// <summary>The dump header here is written from the layout (DUMP_HEADER64), not copied from a real crash.</summary>
public class BugCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), FileTime = new(2026, 10, 1, 8, 5, 0, TimeSpan.Zero);
    private static byte[] Dump64(uint code, ulong p1, DateTimeOffset? at = null, TimeSpan? up = null, int length = 0x1040)
    {
        var d = new byte[length]; "PAGEDU64"u8.CopyTo(d);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x38), code); BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(0x40), p1); BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(0x58), 0x44);
        if (length >= 0x1038)
        {
            if (at is { } t) BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(0xFA8), t.UtcDateTime.ToFileTimeUtc());
            if (up is { } u) BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(0x1030), u.Ticks);
        }
        return d;
    }

    [Fact] public void A_64_bit_dump_gives_its_code_parameters_time_and_uptime()
    {
        var at = new DateTimeOffset(2026, 9, 30, 21, 42, 0, TimeSpan.Zero);
        var c = BugCheckCatalog.ReadDump(Dump64(0x124, 0, at, TimeSpan.FromMinutes(47)), @"C:\Windows\Minidump\093026-8406-01.dmp", FileTime, Now)!;
        Assert.Equal((0x124u, at, false, TimeSpan.FromMinutes(47)), (c.Code, c.At, c.AtIsRestart, c.Uptime!.Value));
        Assert.Equal([0UL, 0UL, 0UL, 0x44UL], c.Parameters);
    }

    [Fact] public void A_time_that_does_not_read_as_one_falls_back_to_the_file_s_and_says_so()
    {
        var c = BugCheckCatalog.ReadDump(Dump64(0x50, 1, Now.AddYears(3), TimeSpan.FromDays(4000)), "a.dmp", FileTime, Now)!;
        Assert.Equal((FileTime, true, (TimeSpan?)null), (c.At, c.AtIsRestart, c.Uptime));
        Assert.True(BugCheckCatalog.ReadDump(Dump64(0x50, 1, length: 0x100), "a.dmp", FileTime, Now)!.AtIsRestart);   // a cut-off header still has its code
    }

    [Fact] public void Anything_that_is_not_a_kernel_dump_is_refused()
    {
        Assert.Null(BugCheckCatalog.ReadDump(new byte[0x1040], "a.dmp", FileTime, Now));
        Assert.Null(BugCheckCatalog.ReadDump("MDMP"u8.ToArray(), "a.dmp", FileTime, Now));
    }

    [Fact] public void Windows_own_log_line_is_parsed_and_a_line_with_only_a_code_keeps_no_parameters()
    {
        var full = BugCheckCatalog.ParseLogText("0x0000009f (0x0000000000000003, 0xffffe0018c8d5060, 0xfffff8037cc3a960, 0xffffe0018c33b010)")!.Value;
        Assert.Equal((0x9Fu, 3UL, 0xffffe0018c33b010UL), (full.Code, full.Parameters![0], full.Parameters[3]));
        var bare = BugCheckCatalog.ParseLogText("0x00000124")!.Value;
        Assert.Equal(0x124u, bare.Code); Assert.Null(bare.Parameters);
        Assert.Null(BugCheckCatalog.ParseLogText(@"C:\WINDOWS\Minidump\100126-1.dmp"));
    }

    [Fact] public void A_code_with_the_reduced_detail_mark_is_its_plain_one_and_an_unknown_code_has_no_entry()
    {
        Assert.Equal("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", BugCheckCatalog.Find(0x1000007E)!.Name);
        Assert.Equal(CrashCause.GpuDriver, BugCheckCatalog.Find(0x116)!.Causes[0]);
        Assert.Null(BugCheckCatalog.Find(0xDEAD));
        Assert.Equal(BugCheckCatalog.All.Count, BugCheckCatalog.All.Select(b => b.Code).Distinct().Count());
    }

    [Fact] public void The_parameters_say_what_the_reference_says_they_say()
    {
        Assert.Equal(["Bsod_Note_Whea_Mce"], BugCheckCatalog.Notes(0x124, [0, 1, 2, 3]));
        Assert.Equal(["Bsod_Note_Whea_Pcie"], BugCheckCatalog.Notes(0x124, [4, 1, 2, 3]));
        Assert.Equal(["Bsod_Note_Io_Cable"], BugCheckCatalog.Notes(0x7A, [1, 0xFFFFFFFFC0000185, 0, 0]));
        Assert.Equal(["Bsod_Note_Io_BadBlocks"], BugCheckCatalog.Notes(0x7A, [1, 0xC000009C, 0, 0]));
        Assert.Equal(["Bsod_Note_AccessViolation"], BugCheckCatalog.Notes(0x1000007E, [0xFFFFFFFFC0000005, 0, 0, 0]));
        Assert.Empty(BugCheckCatalog.Notes(0x124, null)); Assert.Empty(BugCheckCatalog.Notes(0x50, [1, 2, 3, 4]));
    }

    [Fact] public void A_crash_with_a_dump_and_a_log_line_is_listed_once_newest_first()
    {
        var at = new DateTimeOffset(2026, 9, 30, 21, 42, 0, TimeSpan.Zero);
        CrashRecord dump = new(at, false, 0x124, [0, 1, 2, 3], @"C:\Windows\Minidump\093026-8406-01.dmp", null);
        CrashRecord same = new(at.AddMinutes(3), true, 0x124, [0, 1, 2, 3], @"C:\WINDOWS\MEMORY.DMP", null), older = new(at.AddDays(-40), true, 0x116, null, null, null);
        var all = BugCheckCatalog.Merge([dump], [same, older]);
        Assert.Equal([dump, older], all);
    }

    [Fact] public void Every_cause_and_every_note_has_its_strings_in_both_languages()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Mazesta.Desktop", "Localization");
        string en = File.ReadAllText(Path.Combine(dir, "Strings.resx")), fa = File.ReadAllText(Path.Combine(dir, "Strings.fa.resx"));
        var keys = Enum.GetNames<CrashCause>().SelectMany(c => new[] { $"Bsod_Cause_{c}", $"Bsod_Cause_{c}_Check" })
            .Concat(BugCheckCatalog.All.Select(b => BugCheckCatalog.ParametersKey(b.Code)).OfType<string>())
            .Concat(new uint[] { 0x124, 0x7A, 0x1A, 0x133, 0x7F, 0x9F, 0x1E, 0x139, 0x101 }.SelectMany(c => new ulong[] { 0, 1, 2, 3, 4, 6, 8, 0xC0000005, 0xC000001D, 0xC000009C, 0xC0000185, 0xC000009A, 0x41790 }
                .SelectMany(v => BugCheckCatalog.Notes(c, [v, v, 0, 0])))).Distinct();
        foreach (string key in keys) { Assert.Contains($"name=\"{key}\"", en); Assert.Contains($"name=\"{key}\"", fa); }
    }
}
