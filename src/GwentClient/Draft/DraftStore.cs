using System.IO;
using System.Text.Json;
using GwentAI.Draft;

namespace GwentClient;

/// <summary>Draft kaydı: süren koşu, en iyi sonuç ve koşu sayısı.</summary>
public sealed class DraftSave
{
    public DraftRun? Run { get; set; }
    public int Best { get; set; }
    public int Runs { get; set; }
}

/// <summary>%AppData%\Gwent\draft.json. Sınamalar ve ekran görüntüleri gerçek kayda dokunmaz (<see cref="PathOverride"/>).</summary>
public static class DraftStore
{
    public static string? PathOverride { get; set; }
    private static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "draft.json");

    public static DraftSave Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<DraftSave>(File.ReadAllText(FilePath)) is { } s)
            {
                if (s.Run is { Version: not 1 }) s.Run = null;
                return s;
            }
        }
        catch { }
        return new DraftSave();
    }

    public static void Save(DraftSave s)
    {
        if (Settings.NoSave && PathOverride == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }
}
