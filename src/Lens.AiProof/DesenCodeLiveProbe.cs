using Lens.Core.Config;
using Lens.Core.DesenCodes;
using Lens.Core.Indexing;

namespace Lens.AiProof;

/// <summary>
/// [CANLI DENEME] Desen kodu servisine, UYGULAMANIN KENDI ISTEMCISIYLE
/// (<see cref="SoapDesenCodeService"/>) az sayida gercek dosya adi sorar.
///
/// Neden ayri bir mod: sahte servisle kosan Grup P testleri ag KULLANMAZ ve
/// gercek servis dogrulamasinin yerine GECMEZ. Bu prob, sozlesmenin canli
/// uc uzerinde gercekten calistigini gosterir.
///
/// GUVENLIK / KAPSAM KURALLARI:
///   • Uc adresi, metot ve parametre adi KODA GOMULU DEGILDIR - exe yanindaki
///     <c>appsettings.json</c>'dan (<see cref="AdminConfig"/>) okunur, yani
///     uretimde kullanilan AYNI yapilandirma yolu dogrulanir.
///   • Yalnizca DOSYA ADI gonderilir; gorsel icerigi, tam yerel yol veya UNC
///     yolu ASLA gonderilmez.
///   • Varsayilan olarak en fazla <see cref="DefaultMaxFiles"/> dosya
///     sorulur - "once az sayida dene" kurali koda baglanmistir; tum
///     kataloga binlerce istek BU MODDAN gonderilemez.
///   • Kimlik bilgisi okunmaz, yazilmaz, loglanmaz.
/// </summary>
public static class DesenCodeLiveProbe
{
    /// <summary>Acik bir sayi verilmezse sorulacak en fazla dosya sayisi.</summary>
    public const int DefaultMaxFiles = 5;

    /// <summary>Bu moddan gonderilebilecek MUTLAK ust sinir - asilirsa kirpilir.</summary>
    public const int HardMaxFiles = 25;

    /// <param name="args">[0] katalog klasoru (zorunlu), [1] kac dosya sorulacagi (opsiyonel).</param>
    public static void Run(string[] args)
    {
        Console.WriteLine("=== Desen kodu CANLI deneme (uygulamanin kendi istemcisiyle) ===");

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Kullanim: desencodelive <katalogKlasoru> [dosyaSayisi]");
            Environment.ExitCode = 2;
            return;
        }

        var folder = args[0];
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"HATA: katalog klasoru bulunamadi: {folder}");
            Environment.ExitCode = 2;
            return;
        }

        var requested = DefaultMaxFiles;
        if (args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed > 0)
        {
            requested = parsed;
        }

        var maxFiles = Math.Min(requested, HardMaxFiles);
        if (requested > HardMaxFiles)
        {
            Console.WriteLine($"UYARI: istenen {requested} dosya, bu modun ust siniri {HardMaxFiles}'e kirpildi.");
        }

        // [Yapilandirma] Uretimle AYNI yol: exe yanindaki appsettings.json.
        var configPath = AppPaths.AdminConfigFilePath;
        Console.WriteLine($"Yapilandirma dosyasi : {configPath}");

        var options = AdminConfig.Load(configPath).DesenCodeService;
        if (options is null || !options.IsConfigured)
        {
            Console.WriteLine("HATA: " + (options ?? new DesenCodeServiceOptions()).DescribeMissingConfiguration());
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine($"Uc                   : {options.Endpoint}");
        Console.WriteLine($"Metot / parametre    : {options.MethodName} / {options.ParameterName}");
        Console.WriteLine($"Namespace            : {options.Namespace}");
        Console.WriteLine($"Uzanti gonderiliyor  : {options.SendFileExtension}");
        Console.WriteLine($"'Bulunamadi'da uzantisiz tekrar: {options.TryWithoutExtensionOnNotFound}");
        Console.WriteLine();

        // Dosya listesi, uygulamanin KULLANDIGI ayni tarama sozlesmesinden gelir.
        var issues = new List<IndexFileIssue>();
        var files = CatalogScanner.EnumerateFiles(folder, issues)
            .Where(f => FileClassifier.Classify(Path.GetExtension(f.FullPath)) == FileClassification.SupportedImage)
            .Select(f => f.RelativePath)
            .Take(maxFiles)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("Bu klasorde desteklenen gorsel bulunamadi - deneme yapilmadi.");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine($"Sorulacak dosya sayisi: {files.Count} (katalogdaki tum dosyalar DEGIL)");
        Console.WriteLine();

        using var service = new SoapDesenCodeService(options);
        int found = 0, notFound = 0, failed = 0;

        foreach (var relativePath in files)
        {
            var fileName = Path.GetFileName(relativePath);
            DesenCodeLookupResult result;
            try
            {
                result = service.LookupAsync(fileName, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                result = DesenCodeLookupResult.Unavailable($"{ex.GetType().Name}: {ex.Message}");
            }

            switch (result.Status)
            {
                case DesenCodeLookupStatus.Found: found++; break;
                case DesenCodeLookupStatus.NotFound: notFound++; break;
                default: failed++; break;
            }

            Console.WriteLine($"  [{result.Status,-19}] {fileName}");
            if (result.Status == DesenCodeLookupStatus.Found)
            {
                // Kod AYNEN yazilir - bastaki sifirlarin korunup korunmadigi
                // BURADAN gorulur.
                Console.WriteLine($"      kod = \"{result.Code}\"  (uzunluk {result.Code!.Length}, sorgulanan ad: {result.QueriedFileName})");
            }
            else if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                Console.WriteLine($"      ayrinti: {result.Detail}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== Sonuc: {found} kod bulundu, {notFound} kod yok, {failed} servis hatasi ===");
        Console.WriteLine(
            "NOT: HTTP 200 veya bos cevap TEK BASINA dogru eslesme kaniti DEGILDIR. "
            + "Beklenen kodu bilinen bir ornek olmadan, donen kodun DOGRU kod oldugu dogrulanmis SAYILMAZ.");
    }
}
