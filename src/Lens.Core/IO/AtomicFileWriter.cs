namespace Lens.Core.IO;

/// <summary>
/// Bir dosyayi once BENZERSIZ isimli gecici bir dosyaya tam olarak yazip
/// sonra yerine tasiyarak (Windows'ta atomic rename/replace) yarim/bozuk
/// dosya birakma riskini azaltir. Uygulama kesintiye ugrarsa onceki gecerli
/// dosya korunur.
///
/// [Shared/UNC hardening] Temp dosya sabit bir isim (orn. "index.json.tmp")
/// DEGIL, her cagrida benzersiz (GUID) bir isim kullanir - ayni ".lens"
/// klasorune birden fazla client (veya ayni client'in art arda cagrilari)
/// eszamanli yazarsa birbirinin gecici dosyasini ezmez. Hedef dosya save
/// basinda SILINMEZ; yalnizca temp tamamen yazilip kapatildiktan SONRA
/// replace/move denenir - basarisiz olursa eski hedef mumkun oldugunca
/// korunur. Temp cleanup best-effort'tur (replace/move basarili olursa zaten
/// temp kalmaz).
/// </summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        var tempPath = string.IsNullOrEmpty(directory)
            ? $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, content);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    // Bazi ag dosya sistemleri (orn. FAT/exFAT paylasimlari)
                    // File.Replace'i desteklemez - overwrite=true ile Move'a
                    // dus (hala "tamamen yazilmis temp -> hedef" sirasini korur).
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            TryDeleteBestEffort(tempPath);
        }
    }

    private static void TryDeleteBestEffort(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Temp cleanup best-effort - kalirsa bir sonraki basarili save
            // etkilenmez (her cagrida yeni GUID uretilir), sadece klasorde
            // zararsiz bir artik dosya kalabilir.
        }
    }
}
