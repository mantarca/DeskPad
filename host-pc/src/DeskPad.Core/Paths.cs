namespace DeskPad.Core;

public static class Paths
{
    /// <summary>Verilen klasor altinda (alt klasorler dahil) ilk eslesen dosyayi bulur.</summary>
    public static string? FindFile(string rootDir, string fileName)
    {
        try
        {
            if (!Directory.Exists(rootDir)) return null;
            foreach (var f in Directory.EnumerateFiles(rootDir, fileName, SearchOption.AllDirectories))
                return f;
        }
        catch
        {
            // erisim hatasi vb. -> yok say
        }
        return null;
    }
}
