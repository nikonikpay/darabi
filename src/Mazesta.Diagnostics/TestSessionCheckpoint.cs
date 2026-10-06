using Mazesta.Persistence;
namespace Mazesta.Diagnostics;

/// <summary>Written before each queue item starts and once more (Completed = true) when the queue ends,
/// cancelled or not. A file that exists with Completed == false on the next launch means the process
/// ended mid-test - crash, reboot, kill (spec §8/§2.5: "پس از crash/reboot فقط ناتمام‌بودن قابل اثبات
/// است؛ دلیل ریبوت بدون شواهد حدس زده نشود"). This type only records that fact; deciding what to show
/// the technician is the Desktop layer's job.</summary>
/// <remarks>Version 2 also keeps how far the running test had got and the results of the tests that had ended, so a broken-off session
/// can say where it stopped and what had already been found - still without guessing why it stopped.</remarks>
public sealed class TestSessionCheckpoint : IVersionedDocument
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SessionId { get; set; } = "";
    public List<string> QueueTestIds { get; set; } = [];
    public int CurrentIndex { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public bool Completed { get; set; }
    /// <summary>The running test's round (from 1) and progress (0-1) when last saved.</summary>
    public int CurrentIteration { get; set; }
    public double CurrentPercent { get; set; }
    /// <summary>The stage the running test was in when last saved (the key of its name), for a test that has stages; else null.</summary>
    public string? CurrentStage { get; set; }
    public List<CheckpointResult> Finished { get; set; } = [];
}

/// <summary>A test of the session that had ended before the checkpoint was saved.</summary>
public sealed class CheckpointResult
{
    public string TestId { get; set; } = "";
    public string Outcome { get; set; } = "";
    public long ErrorCount { get; set; }
}

/// <summary>Version 1 had no progress or results: an empty list, and no progress known.</summary>
public sealed class CheckpointV1ToV2 : IMigration
{
    public int From => 1;
    public System.Text.Json.Nodes.JsonObject Apply(System.Text.Json.Nodes.JsonObject document) { document["finished"] ??= new System.Text.Json.Nodes.JsonArray(); return document; }
}
