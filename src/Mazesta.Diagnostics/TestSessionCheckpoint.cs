using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

/// <summary>Written before each queue item starts and once more (Completed = true) when the queue ends,
/// cancelled or not. A file that exists with Completed == false on the next launch means the process
/// ended mid-test - crash, reboot, kill (spec §8/§2.5: "پس از crash/reboot فقط ناتمام‌بودن قابل اثبات
/// است؛ دلیل ریبوت بدون شواهد حدس زده نشود"). This type only records that fact; deciding what to show
/// the technician is the Desktop layer's job.</summary>
public sealed class TestSessionCheckpoint : IVersionedDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SessionId { get; set; } = "";
    public List<string> QueueTestIds { get; set; } = [];
    public int CurrentIndex { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public bool Completed { get; set; }
}
