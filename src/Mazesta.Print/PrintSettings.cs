using System.Text.Json;
namespace Mazesta.Print;

/// <summary>What the program keeps: the site's reading key and the paper layout, in Data next to the exe (portable, like Mazesta itself).</summary>
internal sealed class PrintSettings
{
    public string Key { get; set; } = "";
    /// <summary>The paper the summary is printed on (see <see cref="SheetLayout"/>).</summary>
    public SheetLayout Layout { get; set; } = SheetLayout.A5;
    private static string File_ => Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Data", "print.json");
    public static string DataDir => Path.GetDirectoryName(File_)!;

    public static PrintSettings Load()
    {
        try { return JsonSerializer.Deserialize<PrintSettings>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        string temp = File_ + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, File_, overwrite: true);
    }
}
