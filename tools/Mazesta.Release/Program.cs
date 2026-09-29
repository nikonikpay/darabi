using System.Diagnostics; using System.IO.Compression; using System.Text.Json;
using Mazesta.Diagnostics.Benchmarks; using Mazesta.Persistence.Updates;

// The shop's release tool. Two commands:
//   keygen --out <folder>            a new signing key pair: the private key to a file there (never into the repository), the public key printed
//   site   --key <private.pem> --out <site folder> [--app <published app folder>] [--notes-fa <file>] [--notes-en <file>]
//          [--runs <Data or runs folder>]... [--archive <folder>]
//          gathers the run logs into the archive (each run once, by its id), builds the comparison lists from the whole archive, zips the app if
//          given (otherwise keeps the release the folder already offers) and writes update.json with its signature. The site folder is then
//          uploaded as it is, to /mazesta/ on the site.
return args.FirstOrDefault() switch
{
    "keygen" => KeyGen(Opt(args, "--out") ?? throw Usage()),
    "site" => Site(args),
    _ => throw Usage(),
};

static int KeyGen(string folder)
{
    Directory.CreateDirectory(folder);
    string file = Path.Combine(folder, "mazesta-update-private.pem");
    if (File.Exists(file)) { Console.Error.WriteLine($"{file} exists; not overwritten (a new key would make every installed copy refuse the next update)."); return 1; }
    var (priv, pub) = UpdateSigning.NewKey();
    File.WriteAllText(file, priv);
    Console.WriteLine($"Private key: {file}  (keep it, and a copy of it, outside the repository)");
    Console.WriteLine($"Public key (src/Mazesta.Web/UpdateKey.cs): {pub}");
    return 0;
}

static int Site(string[] args)
{
    string key = File.ReadAllText(Opt(args, "--key") ?? throw Usage());
    string outDir = Path.GetFullPath(Opt(args, "--out") ?? throw Usage());
    string archive = Path.GetFullPath(Opt(args, "--archive") ?? Path.Combine(outDir, "..", "bench-archive"));
    Directory.CreateDirectory(outDir);

    // 1. The runs, gathered into the archive: each copy's run logs may be brought in again and again, a run is added once.
    var known = BenchmarkRunLog.ReadFolder(archive).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
    int added = 0;
    // The shop's marks (featured, overclocked) come along with the runs; for a run marked on more than one copy, the latest mark wins.
    string marksFile = Path.Combine(archive, BenchmarkMarks.FileName);
    var markSets = new List<IReadOnlyDictionary<string, BenchmarkMark>> { BenchmarkMarks.Read(marksFile) };
    foreach (var from in Opts(args, "--runs"))
    {
        string folder = Directory.Exists(Path.Combine(from, "benchmarks", "runs")) ? Path.Combine(from, "benchmarks", "runs") : from;
        markSets.Add(BenchmarkMarks.Read(Path.Combine(folder, BenchmarkMarks.FileName)));
        foreach (var run in BenchmarkRunLog.ReadFolder(folder).Where(r => known.Add(r.Id)))
        {
            Directory.CreateDirectory(archive);
            File.AppendAllText(Path.Combine(archive, $"{run.At.UtcDateTime:yyyy-MM}.jsonl"), JsonSerializer.Serialize(run, Json) + "\n");
            added++;
        }
    }
    var all = BenchmarkRunLog.ReadFolder(archive).ToList();
    var marks = BenchmarkMarks.Merge(markSets);
    if (marks.Count > 0) { Directory.CreateDirectory(archive); File.WriteAllText(marksFile, JsonSerializer.Serialize(marks, Json)); }
    Console.WriteLine($"Archive {archive}: {added} new runs, {all.Count} in all; {marks.Values.Count(m => m.Featured)} featured");

    // 2. The comparison lists, one file per benchmark, version and settings; lists no run supports any more are removed.
    string db = Path.Combine(outDir, "benchdb");
    Directory.CreateDirectory(db);
    var tables = BenchmarkPeers.Aggregate(all, DateTimeOffset.UtcNow, marks);
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var t in tables)
    {
        string name = BenchmarkPeers.FileName(t.Key); names.Add(name);
        File.WriteAllText(Path.Combine(db, name), BenchmarkPeers.Write(t));
        Console.WriteLine($"  {t.Key}: {t.Entries.Count} models");
    }
    foreach (var stale in Directory.EnumerateFiles(db, "*.json").Where(f => !names.Contains(Path.GetFileName(f)))) File.Delete(stale);

    // 3. The release: a new zip when an app folder is given, otherwise the one update.json already offers.
    AppRelease? release = null;
    string manifestFile = Path.Combine(outDir, UpdateManifest.FileName);
    if (Opt(args, "--app") is { } appDir)
    {
        string exe = Path.Combine(appDir, UpdateInstaller.ExeName);
        string version = FileVersionInfo.GetVersionInfo(exe).FileVersion is { } v && Version.TryParse(v, out var parsed) ? parsed.ToString(3) : throw new InvalidOperationException($"No version in {exe}");
        string zipName = $"MazestaWeb-{version}.zip", zip = Path.Combine(outDir, zipName);
        File.Delete(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (var file in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(appDir, file).Replace('\\', '/');
                if (rel.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) continue;   // the owner's settings and reports never ship
                z.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
            }
        foreach (var old in Directory.EnumerateFiles(outDir, "MazestaWeb-*.zip").Where(f => Path.GetFileName(f) != zipName)) File.Delete(old);
        release = new AppRelease(version, zipName, new FileInfo(zip).Length, UpdateSigning.Sha256(zip), DateTimeOffset.UtcNow,
            Opt(args, "--notes-fa") is { } fa ? File.ReadAllText(fa).Trim() : null, Opt(args, "--notes-en") is { } en ? File.ReadAllText(en).Trim() : null);
        Console.WriteLine($"Release {version}: {zipName}, {release.Size / 1048576.0:0.0} MB");
    }
    else if (File.Exists(manifestFile))
    {
        release = UpdateManifest.Parse(File.ReadAllBytes(manifestFile)).App;
        if (release is not null && !File.Exists(Path.Combine(outDir, release.File))) { Console.Error.WriteLine($"{release.File} is missing; the manifest offers no release."); release = null; }
    }

    // 4. The manifest and its signature, checked once more the way the app will check it.
    var data = names.Order(StringComparer.Ordinal).Select(n => Path.Combine(db, n)).Select(f => new UpdateFile(DataSync.Prefix + Path.GetFileName(f), new FileInfo(f).Length, UpdateSigning.Sha256(f))).ToList();
    var manifest = new UpdateManifest(UpdateManifest.CurrentFormat, DateTimeOffset.UtcNow, release, data);
    var bytes = manifest.ToBytes();
    string sig = UpdateSigning.Sign(bytes, key);
    File.WriteAllBytes(manifestFile, bytes);
    File.WriteAllText(Path.Combine(outDir, UpdateManifest.SignatureName), sig);
    UpdateManifest.Parse(bytes);
    Console.WriteLine($"Signed {manifestFile}: {(release is null ? "no release" : "release " + release.Version)}, {data.Count} lists. Upload the folder's contents to /mazesta/ on the site.");
    return 0;
}

static string? Opt(string[] args, string name) => Opts(args, name).LastOrDefault();
static IEnumerable<string> Opts(string[] args, string name) { for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) yield return args[i + 1]; }
static ArgumentException Usage() => new("usage: mazesta-release keygen --out <folder> | site --key <pem> --out <folder> [--app <folder>] [--notes-fa <file>] [--notes-en <file>] [--runs <folder>]... [--archive <folder>]");

internal static partial class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
