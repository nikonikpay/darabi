using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Mazesta.Hardware.Tests;

public class WmiInventoryProviderTests
{
    private sealed class FakeWmiQuery : IWmiQuery
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql)
        {
            if (wql.Contains("Win32_Processor"))
                return [new Dictionary<string, object?> { ["NumberOfCores"] = "many" }];
            if (wql.Contains("MSFT_PhysicalDisk"))
                throw new InvalidOperationException("no storage namespace");
            return [];
        }
    }

    [Fact]
    public async Task Parse_or_query_failure_degrades_only_that_section()
    {
        var provider = new WmiInventoryProvider(new FakeWmiQuery(), NullLogger<WmiInventoryProvider>.Instance);

        var inv = await provider.ReadAsync(CancellationToken.None);

        Assert.Null(inv.Cpu);
        Assert.Empty(inv.Storage);
        Assert.Contains(inv.Errors, e => e.StartsWith("cpu:"));
        Assert.Contains(inv.Errors, e => e.StartsWith("disks:"));
        Assert.Null(inv.Os);
        Assert.Empty(inv.Gpus);
    }
}
