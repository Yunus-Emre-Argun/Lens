namespace Lens.Core.Indexing;

/// <summary>Bir taramanin urettigi dosya kaydi.</summary>
/// <param name="FullPath">Diskteki tam yol.</param>
/// <param name="RelativePath">Katalog kokune gore, her zaman '/' ayracli goreli yol - index'in KALICI anahtaridir.</param>
public readonly record struct ScannedFile(string FullPath, string RelativePath);

/// <summary>
/// Katalog klasorunun ALT KLASORLERLE BIRLIKTE taranmasi.
///
/// Onceki surumde yalnizca ust dizin taraniyordu
/// (<c>Directory.EnumerateFiles</c>). Alt klasor destegi eklenirken uc sey
/// kritik oldu:
///
///   1. <b>Kayit anahtari degisti:</b> artik dosya adi degil, katalog kokune
///      gore GORELI YOL. Boylece farkli alt klasorlerdeki ayni adli
///      gorseller AYRI kayitlar olur. Ayrac her zaman '/' - Windows'ta
///      uretilen index Linux/UNC yollarinda da ayni okunur ve mevcut
///      kayitlarla tutarli kalir.
///   2. <b>Kok seviyesi geriye uyumlu:</b> kokteki bir dosyanin goreli yolu
///      yine sadece "a.jpg"dir; boylece kullanicinin mevcut ~5.000 gorsellik
///      DINO index'i GECERLI kalir ve yeniden embed EDILMEZ.
///   3. <b>Dongu korumasi:</b> junction/symlink/reparse point takip
///      EDILMEZ - aksi halde kendini iceren bir baglanti sonsuz doguya
///      yol acardi.
/// </summary>
public static class CatalogScanner
{
    /// <summary>Lens'in kendi teknik klasoru - taranmaz (index, kilit ve metadata buradadir).</summary>
    private const string TechnicalFolderName = ".lens";

    /// <summary>
    /// Guvenlik siniri: bu derinligin otesine inilmez. Beklenmedik bir
    /// yapida (veya dongu korumasini asan bir kurulumda) taramanin sonsuza
    /// kadar surmesini engeller.
    /// </summary>
    public const int MaxDepth = 16;

    /// <param name="rootPath">Katalog koku.</param>
    /// <param name="issues">Erisilemeyen ALT klasorler buraya eklenir - tarama COKMEZ.</param>
    /// <exception cref="Exception">
    /// KOK klasorun kendisine erisilemezse (orn. UNC yolu tamamen dustu)
    /// exception YUKARI TASINIR. Bu bilincli bir ayrimdir: cagiran taraf
    /// (<see cref="ImageIndex.BuildOrUpdate"/>) bunu <c>ScanError</c> olarak
    /// isler ve MEVCUT INDEX'I DEGISTIRMEDEN birakir. Sessizce "hic dosya
    /// yok" donmek, tum kayitlarin "silinmis" sayilmasina ve kullanicinin
    /// ~5.000 gorsellik index'inin kaybina yol acardi (bkz.
    /// docs/DECISIONS.md #55, #57).
    ///
    /// ALT klasorlerdeki erisim sorunlari ise tum islemi durdurmaz -
    /// yalnizca <paramref name="issues"/> listesine eklenir.
    /// </exception>
    public static List<ScannedFile> EnumerateFiles(string rootPath, List<IndexFileIssue> issues)
    {
        var results = new List<ScannedFile>();
        var rootFull = Path.GetFullPath(rootPath);

        // [Kok erisim kontrolu] Koke hic ulasilamiyorsa bu bir TARAMA
        // HATASIDIR, "bos katalog" degildir. EnumerateDirectories cagrisi
        // erisilemeyen/olmayan yolda exception firlatir ve bu bilerek
        // yakalanMAZ.
        _ = Directory.EnumerateDirectories(rootFull).Any();

        // Ayni fiziksel klasore iki yoldan ulasilirsa ikinci kez inilmez.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Walk(rootFull, rootFull, 0, results, issues, visited, isRoot: true);
        results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
        return results;
    }

    private static void Walk(
        string rootFull, string currentPath, int depth,
        List<ScannedFile> results, List<IndexFileIssue> issues, HashSet<string> visited,
        bool isRoot = false)
    {
        if (depth > MaxDepth)
        {
            issues.Add(new IndexFileIssue(
                DescribeRelative(rootFull, currentPath), string.Empty, FileIssueKind.NonImageFile,
                $"Klasör derinliği sınırı aşıldı ({MaxDepth}) - bu dal taranmadı"));
            return;
        }

        if (!visited.Add(currentPath))
        {
            return;
        }

        // ---- Dosyalar ----
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(currentPath))
            {
                var relative = DescribeRelative(rootFull, filePath);

                // Katalog disina cikan bir yol (beklenmez ama savunmaci)
                // KABUL EDILMEZ.
                if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                {
                    continue;
                }

                results.Add(new ScannedFile(filePath, relative));
            }
        }
        catch (Exception) when (isRoot)
        {
            // KOK okunamiyorsa bu bir tarama hatasidir - yukari tasinir ki
            // mevcut index KORUNSUN (bkz. metot aciklamasi).
            throw;
        }
        catch (Exception ex)
        {
            // Erisilemeyen ALT klasor TUM islemi cokertmez - sorunlu
            // listesine eklenir ve tarama devam eder.
            issues.Add(new IndexFileIssue(
                DescribeRelative(rootFull, currentPath), string.Empty, FileIssueKind.NonImageFile,
                $"Klasör okunamadı: {ex.Message}"));
            return;
        }

        // ---- Alt klasorler ----
        IEnumerable<string> subdirectories;
        try
        {
            subdirectories = Directory.EnumerateDirectories(currentPath).ToList();
        }
        catch (Exception) when (isRoot)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add(new IndexFileIssue(
                DescribeRelative(rootFull, currentPath), string.Empty, FileIssueKind.NonImageFile,
                $"Alt klasörler listelenemedi: {ex.Message}"));
            return;
        }

        foreach (var directory in subdirectories)
        {
            var name = Path.GetFileName(directory);

            // Lens'in kendi teknik klasoru taranmaz.
            if (string.Equals(name, TechnicalFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsReparsePoint(directory))
            {
                // Junction/symlink TAKIP EDILMEZ - dongu riski. Kullanicinin
                // haberi olsun diye sorunlu listesine eklenir.
                issues.Add(new IndexFileIssue(
                    DescribeRelative(rootFull, directory), string.Empty, FileIssueKind.NonImageFile,
                    "Bağlantı (junction/symlink) - döngü riski nedeniyle taranmadı"));
                continue;
            }

            Walk(rootFull, directory, depth + 1, results, issues, visited);
        }
    }

    /// <summary>Junction, symlink veya baska bir reparse point mi?</summary>
    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // Ozniteligi okuyamiyorsak temkinli davranip TARAMAYIZ.
            return true;
        }
    }

    /// <summary>
    /// Katalog kokune gore, her zaman '/' ayracli goreli yol. Kokteki bir
    /// dosya icin sonuc yalnizca dosya adidir - mevcut index kayitlariyla
    /// BIREBIR uyumlu kalir.
    /// </summary>
    public static string DescribeRelative(string rootFull, string fullPath)
    {
        var relative = Path.GetRelativePath(rootFull, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
    }
}
