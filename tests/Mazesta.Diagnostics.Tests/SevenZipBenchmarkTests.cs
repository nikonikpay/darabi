using Xunit; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Tests;

public class SevenZipBenchmarkTests
{
    // The end of 7-Zip 24.08's "7z b" on a 16-thread CPU, as it prints it.
    private const string Output = """

        7-Zip 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-08-11

        Windows 10.0 22631 : x64-0-0
        CPU Freq: - - - - - - - - -

        RAM size:   32604 MB,  # CPU hardware threads:  16
        RAM usage:   3530 MB,  # Benchmark threads:     16

                               Compressing  |                  Decompressing
        Dict     Speed Usage    R/U Rating  |      Speed Usage    R/U Rating
                 KiB/s     %   MIPS   MIPS  |      KiB/s     %   MIPS   MIPS

        22:     119846  1361   8566 116587  |    1484932  1556   8140 126613
        23:     112331  1404   8152 114452  |    1465640  1560   8132 126845
        24:     106457  1435   7977 114464  |    1440040  1569   8056 126386
        25:      98989  1452   7783 113023  |    1395017  1552   8000 124142
        ----------------------------------  | ------------------------------
        Avr:            1413   8120 114632  |              1559   8082 125997
        Tot:            1486   8101 120314
        """;

    [Fact] public void Reads_the_average_and_total_ratings() => Assert.Equal((114632d, 125997d, 120314d), SevenZipBenchmark.Parse(Output));

    [Fact] public void Output_without_the_rating_lines_has_no_result() => Assert.Null(SevenZipBenchmark.Parse("7-Zip 24.08\nERROR: Can't allocate required memory!"));
}
