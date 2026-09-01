namespace Lens.Core.IO;

/// <summary>
/// Bir dosyayi once gecici bir dosyaya yazip sonra yerine tasiyarak
/// (Windows'ta atomic rename) yarim/bozuk dosya birakma riskini azaltir.
/// Uygulama kesintiye ugrarsa onceki gecerli dosya korunur.
/// </summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
