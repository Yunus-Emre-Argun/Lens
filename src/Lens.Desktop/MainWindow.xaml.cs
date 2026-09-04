using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lens.Core.Ai;
using Lens.Core.Config;
using Lens.Core.Indexing;
using Lens.Core.Logging;
using Lens.Core.Search;
using Microsoft.Win32;

namespace Lens.Desktop;

/// <summary>
/// Faz 3B/4A: tek ekranli WPF MVP. AI/index katmani (Lens.Core) Faz 3A'da
/// dogrulanan haliyle degistirilmeden kullanilir; bu dosya yalnizca UI
/// orkestrasyonunu yapar (MVVM framework kasitli olarak kullanilmadi - YAGNI).
/// </summary>
public partial class MainWindow : Window
{
    private enum DirectoryOrigin { None, AdminDefault, UserOverride, Manual }

    /// <summary>
    /// [Faz 4B] Search-before-refresh throttle: bu sureden kisa bir sure once
    /// zaten bir freshness kontrolu/tarama yapildiysa "Ara" tekrar taramaz.
    /// </summary>
    private static readonly TimeSpan FreshnessCheckInterval = TimeSpan.FromSeconds(30);

    private string? _productFolder;
    private string? _queryImagePath;
    private ClipEmbedder? _embedder;
    private List<ImageIndexEntry> _indexEntries = new();
    private readonly ObservableCollection<SearchResultViewModel> _results = new();
    private DirectoryOrigin _directoryOrigin = DirectoryOrigin.None;
    private DateTime? _lastFreshnessCheckUtc;
    private readonly ILensLogger _logger = new FileLogger();
    private IReadOnlyList<IndexFileIssue> _lastIssues = Array.Empty<IndexFileIssue>();
    private SearchResultViewModel? _selectedResult;
    private ImagePreviewWindow? _openPreview;
    private DragPreviewAdorner? _dragPreviewAdorner;

    /// <summary>[Faz 1] Son BAŞARILI taramanın istatistikleri - başarısız bir tarama bunu değiştirmez (bkz. RunIndexUpdateAsync / UpdateStatsUi, Faz 2).</summary>
    private IndexUpdateStats? _lastSuccessfulStats;

    /// <summary>[Tema turu] Aktif tema - menu check-state'i ve tema degisiminde ekranda zaten
    /// gorunen (imperatif atanmis) renklerin yeniden uygulanmasi icin tutulur.</summary>
    private AppTheme _currentTheme = AppTheme.Normal;

    /// <summary>[Tema turu] SetIndexStatus'un en son success parametresi - tema degistiginde
    /// IndexStatusText.Foreground'u METNE DOKUNMADAN yeniden hesaplamak icin (bkz.
    /// RefreshThemeDependentForegrounds).</summary>
    private bool? _lastIndexStatusSuccess;

    public MainWindow()
    {
        InitializeComponent();
        ResultsItemsControl.ItemsSource = _results;
        _logger.Info("AppStart");

        var userSettings = UserSettings.Load(_logger);
        AutoIndexCheckBox.IsChecked = userSettings.AutoIndexBeforeSearch;
        // [Tema turu] persist:false - acilista SADECE kayitli tercih uygulanir, tekrar
        // diske YAZILMAZ (bkz. talimat "acilista tema yukleme olaylari yanlislikla
        // varsayilani kaydedip mevcut tercihi ezmemeli").
        SetTheme(ParseTheme(userSettings.Theme), persist: false);
        UpdateStatsUi();

        // [Reliability] Varsayilan dizin bir UNC yol olabilir ve erisim
        // kontrolu (Directory.Exists) yavas/askida kalabilir - constructor'i
        // (dolayisiyla pencerenin ilk gorunmesini) BLOKLAMAMASI icin arka
        // planda calistirilir. Fire-and-forget ama kendi ici try/catch'li.
        _ = LoadDefaultProductDirectoryAsync();
    }

    /// <summary>[Faz 1] Checkbox tercihi degistiginde aninda kalicilastirilir (bkz. UserSettings.AutoIndexBeforeSearch).</summary>
    private void AutoIndexCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        var settings = UserSettings.Load(_logger);
        settings.AutoIndexBeforeSearch = AutoIndexCheckBox.IsChecked == true;
        settings.Save(_logger);
    }

    /// <summary>
    /// [Faz 4D] Ana ekrandaki durum metnini, sonucun niteligine gore hafif bir
    /// renk vurgusuyla gosterir (success=yesil, warning/hata=kirmizimsi,
    /// null=notr). Salt UI vurgusu - IndexUpdateStats/log icerigini etkilemez.
    /// </summary>
    private void SetIndexStatus(string text, bool? success = null)
    {
        IndexStatusText.Text = text;
        _lastIndexStatusSuccess = success;
        ApplyIndexStatusForeground();
    }

    private void ApplyIndexStatusForeground()
    {
        IndexStatusText.Foreground = _lastIndexStatusSuccess switch
        {
            true => (Brush)FindResource("SuccessBrush"),
            false => (Brush)FindResource("WarningBrush"),
            null => (Brush)FindResource("NeutralTextBrush"),
        };
    }

    /// <summary>[Tema turu] user-settings.json'daki serbest string'i guvenle AppTheme'e
    /// cevirir - bos/bilinmeyen/gecersiz deger her zaman Normal'e duser, hicbir istisna
    /// firlatmaz (diger ayarlari etkilemez).</summary>
    private static AppTheme ParseTheme(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(typeof(AppTheme), parsed))
        {
            return parsed;
        }

        return AppTheme.Normal;
    }

    /// <summary>
    /// [Tema turu] Temayi UYGULAR (Resources[...] icindeki renk kaynaklarini degistirir,
    /// menudeki check isaretini gunceller, ekranda ZATEN gorunen imperatif renkleri
    /// yeniden hesaplar) ve istenirse KALICI hale getirir. Arama/threshold/indeksleme/
    /// urun klasoru/tarama istatistiklerine KESINLIKLE dokunmaz - sadece renk.
    /// </summary>
    private void SetTheme(AppTheme theme, bool persist)
    {
        _currentTheme = theme;

        var colors = ThemePalette.For(theme);
        Resources["MainBackgroundBrush"] = new SolidColorBrush(colors.MainBackground);
        Resources["NeutralTextBrush"] = new SolidColorBrush(colors.NeutralText);
        Resources["SectionHeaderBrush"] = new SolidColorBrush(colors.SectionHeader);
        Resources["SecondaryTextBrush"] = new SolidColorBrush(colors.SecondaryText);
        Resources["SuccessBrush"] = new SolidColorBrush(colors.Success);
        Resources["WarningBrush"] = new SolidColorBrush(colors.Warning);

        UpdateThemeMenuChecks(theme);
        RefreshThemeDependentForegrounds();

        if (persist)
        {
            // [Tema turu] Load->degistir->Save: AutoIndexBeforeSearch ve kullanici
            // klasoru override'i gibi diger alanlar bu sayede KAYBOLMAZ. Bu, salt
            // kullanicinin KENDI bilgisayarindaki LocalAppData dosyasidir - shared
            // index/urun klasorune hicbir sey yazilmaz.
            var settings = UserSettings.Load(_logger);
            settings.Theme = theme.ToString();
            settings.Save(_logger);
        }
    }

    private void UpdateThemeMenuChecks(AppTheme theme)
    {
        ThemeMenuItem_Acik.IsChecked = theme == AppTheme.Acik;
        ThemeMenuItem_Normal.IsChecked = theme == AppTheme.Normal;
        ThemeMenuItem_Koyu.IsChecked = theme == AppTheme.Koyu;
        ThemeMenuItem_AcikSepya.IsChecked = theme == AppTheme.AcikSepya;
        ThemeMenuItem_KoyuSepya.IsChecked = theme == AppTheme.KoyuSepya;
        ThemeMenuItem_Lime.IsChecked = theme == AppTheme.Lime;
    }

    /// <summary>
    /// [Tema turu] Ekranda ZATEN gorunen, kod-arkasindan imperatif atanmis renkleri
    /// (DynamicResource'un otomatik guncelleyemeyecegi degerleri) mevcut durumdan
    /// (secili sonuc/son index durumu) yeniden hesaplar. Metni/secimi/state'i
    /// DEGISTIRMEZ - yalnizca Foreground.
    /// </summary>
    private void RefreshThemeDependentForegrounds()
    {
        ApplyIndexStatusForeground();

        ComparisonScoreText.Foreground = _selectedResult is { IsPerfectMatch: true }
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("NeutralTextBrush");
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            SetTheme(ParseTheme(tag), persist: true);
        }
    }

    /// <summary>
    /// [Scroll fix] Yeni bir sonuc kumesi/karsilastirma durumu goruntulendiginde sonuc
    /// viewport'unu en basa dondurur. ScrollToTop hedefi (offset 0) her zaman gecerlidir
    /// (mevcut extent'e gore clamp gerekmez), ama WPF'in ItemsControl icerik degisikliginden
    /// sonraki layout gecisini guvenle bekleyebilmek icin Dispatcher.Loaded onceligiyle
    /// TEK SEFERLIK planlanir. Bu bir LayoutUpdated/ScrollChanged ABONELIGI DEGILDIR -
    /// kullanicinin sonradan yaptigi manuel kaydirmayi asla geri almaz.
    /// </summary>
    private void ResetResultsScroll()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ResultsScrollViewer.ScrollToTop();
            ResultsScrollViewer.ScrollToHorizontalOffset(0);
        }));
    }

    /// <summary>
    /// [Faz 4A] Acilista admin default / kullanici override'i coz, erisilebilirse
    /// otomatik yukle. Indeksleme burada TETIKLENMEZ - klasor hazir gelir,
    /// kullanici "Indeksi Guncelle" ile taramayi kendisi baslatir.
    ///
    /// [Reliability] UNC erisim kontrolu (ResolveDefault icindeki
    /// Directory.Exists) yavas olabilecegi icin arka planda (Task.Run)
    /// calistirilir; devami (await sonrasi) otomatik olarak UI thread'e
    /// doner (WPF SynchronizationContext), bu yuzden asagidaki UI erisimleri
    /// degismeden kalabilir. Beklenmeyen bir hata olursa kullanici dostu bir
    /// durum mesaji gosterilir, uygulama cokmez.
    /// </summary>
    private async Task LoadDefaultProductDirectoryAsync()
    {
        ProductDirectoryResolution resolution;
        try
        {
            resolution = await Task.Run(() => ProductDirectoryResolver.ResolveDefault(_logger));
        }
        catch (Exception ex)
        {
            SetIndexStatus("Varsayılan ürün dizinine ulaşılamadı. Lütfen bir klasör seçin.", success: false);
            _logger.Error("ProductDirectory", reason: ex.Message);
            UpdateDirectoryOriginUi();
            return;
        }

        if (resolution.Directory is null)
        {
            SetIndexStatus("Varsayılan ürün dizini yapılandırılmamış. Lütfen bir klasör seçin.");
            _logger.Info("ProductDirectory", reason: "yapılandırılmamış");
            UpdateDirectoryOriginUi();
            return;
        }

        if (!resolution.IsAccessible)
        {
            SetIndexStatus(
                $"Varsayılan ürün dizinine ulaşılamadı: {resolution.Directory}\nLütfen başka bir klasör seçin.",
                success: false);
            _logger.Warning("ProductDirectory", file: resolution.Directory, reason: "erişilemedi");
            UpdateDirectoryOriginUi();
            return;
        }

        _productFolder = resolution.Directory;
        _logger.Info("ProductDirectory", file: _productFolder, reason: resolution.Source.ToString());
        FolderPathTextBox.Text = _productFolder;

        SetIndexStatus("Paylaşılan index yükleniyor...");
        var folder = _productFolder;
        // [Faz 1 - shared index network safety] Shared index artik urun
        // klasorunun kendi icinde (UNC olabilir) - File.Exists/Load burada da
        // arka planda calistirilmali (bkz. proje talimati madde 7).
        var (hadCacheFile, loadedEntries) = await Task.Run(() =>
            (File.Exists(ImageIndex.IndexPath(folder)), ImageIndex.Load(folder, _logger)));
        _indexEntries = loadedEntries;
        _lastFreshnessCheckUtc = null;
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        SetIndexStatus(_indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : hadCacheFile
                // [Reliability] Cache dosyasi vardi ama Load onu gecersiz
                // bulup reddetti (bozuk/uyumsuz) - kullanicinin "ilk kullanim"
                // ile "bozuk cache" durumlarini ayirt edebilmesi icin farkli
                // bir mesaj gosterilir. Guvenli cozum: "Indeksi Guncelle" ile
                // normal rebuild.
                ? "Kayıtlı index okunamadı (bozuk veya uyumsuz). 'İndeksi Güncelle' ile yeniden oluşturabilirsiniz."
                : "Varsayılan klasör yüklendi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.");

        _directoryOrigin = resolution.Source == ProductDirectorySource.UserOverride
            ? DirectoryOrigin.UserOverride
            : DirectoryOrigin.AdminDefault;
        UpdateDirectoryOriginUi();
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Ürün Klasörünü Seçin" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _productFolder = dialog.FolderName;
        FolderPathTextBox.Text = _productFolder;
        _results.Clear();
        ClearComparison();
        ResetResultsScroll();
        _lastFreshnessCheckUtc = null;
        _lastSuccessfulStats = null;
        UpdateStatsUi();

        SetBusy(true);
        SetIndexStatus("Paylaşılan index yükleniyor...");
        var folder = _productFolder;
        // [Faz 1 - shared index network safety] bkz. LoadDefaultProductDirectoryAsync.
        var (hadCacheFile, loadedEntries) = await Task.Run(() =>
            (File.Exists(ImageIndex.IndexPath(folder)), ImageIndex.Load(folder, _logger)));
        SetBusy(false);

        _indexEntries = loadedEntries;
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        SetIndexStatus(_indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : hadCacheFile
                ? "Kayıtlı index okunamadı (bozuk veya uyumsuz). 'İndeksi Güncelle' ile yeniden oluşturabilirsiniz."
                : "Klasör seçildi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.");

        // [Faz 4A] Manuel secim varsayilan olarak GECICIDIR (session-only) -
        // burada hicbir ayar dosyasina yazilmaz. Kalici hale getirmek icin
        // kullanici "Bu Klasoru Varsayilan Yap" butonuna basmali.
        _directoryOrigin = DirectoryOrigin.Manual;
        UpdateDirectoryOriginUi();
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (_productFolder is null)
        {
            return;
        }

        ProductDirectoryResolver.SetUserOverride(_productFolder, _logger);
        _logger.Info("UserOverride", file: _productFolder, reason: "set");
        _directoryOrigin = DirectoryOrigin.UserOverride;
        UpdateDirectoryOriginUi();
        SetIndexStatus("Bu klasör kalıcı varsayılan olarak ayarlandı.", success: true);
    }

    private void ClearDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        ProductDirectoryResolver.ClearUserOverride(_logger);
        _logger.Info("UserOverride", reason: "cleared");
        _directoryOrigin = _productFolder is null ? DirectoryOrigin.None : DirectoryOrigin.Manual;
        UpdateDirectoryOriginUi();
        SetIndexStatus("Kullanıcı varsayılanı temizlendi. Sonraki açılışta yönetici varsayılanı kullanılacak.");
    }

    private void UpdateDirectoryOriginUi()
    {
        DirectorySourceText.Text = _directoryOrigin switch
        {
            DirectoryOrigin.AdminDefault => "(Yönetici varsayılanı)",
            DirectoryOrigin.UserOverride => "(Kullanıcı varsayılanı)",
            DirectoryOrigin.Manual => "(Geçici seçim)",
            _ => string.Empty,
        };
        SetDefaultButton.IsEnabled = _productFolder is not null && _directoryOrigin != DirectoryOrigin.UserOverride;
        ClearDefaultButton.Visibility = _directoryOrigin == DirectoryOrigin.UserOverride
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void UpdateIndexButton_Click(object sender, RoutedEventArgs e)
    {
        if (_productFolder is null)
        {
            AlertWindow.Show(this, "Önce bir ürün klasörü seçin.", "Klasör seçilmedi", AlertKind.Warning);
            return;
        }

        SetBusy(true);
        SetIndexStatus("Klasör kontrol ediliyor...");

        // [Reliability] Bu on-kontrol daha once UI thread'de senkron
        // calisiyordu - UNC yol yavas/erisilemezse pencereyi donduruyordu.
        // Artik arka planda calisir ve olasi bir erisim hatasi burada
        // yakalanip kullanici dostu mesaja cevrilir (uygulama cokmez).
        bool hasSupportedImage;
        try
        {
            var folder = _productFolder;
            hasSupportedImage = await Task.Run(() =>
                Directory.EnumerateFiles(folder)
                    .Any(f => FileClassifier.Classify(Path.GetExtension(f)) == FileClassification.SupportedImage));
        }
        catch (Exception ex)
        {
            SetBusy(false);
            SetIndexStatus("Ürün klasörüne şu anda ulaşılamıyor.", success: false);
            _logger.Warning("IndexPreflight", file: _productFolder, reason: ex.Message);
            AlertWindow.Show(this, "Ürün klasörüne şu anda ulaşılamıyor.", "Klasöre ulaşılamıyor", AlertKind.Warning);
            return;
        }

        if (!hasSupportedImage)
        {
            SetBusy(false);
            AlertWindow.Show(this, "Bu klasörde desteklenen görsel (jpg/jpeg/png) bulunamadı.",
                "Görsel bulunamadı", AlertKind.Warning);
            return;
        }

        if (!TryEnsureEmbedder(out var modelError))
        {
            SetBusy(false);
            AlertWindow.Show(this, modelError, "Model yüklenemedi", AlertKind.Error);
            return;
        }

        SetIndexStatus("İndeksleniyor...");

        // Manuel "İndeksi Güncelle" her zaman FORCE SCAN yapar (freshness
        // kontrolünü atlar) VE checkbox tercihinden BAĞIMSIZ olarak çalışır.
        // Bu, arama öncesi otomatik freshness-check'in ("Ara") de aynı işlevi
        // görecek olmasından bağımsızdır.
        await RunIndexUpdateAsync(trigger: "Manual");
        SetBusy(false);
    }

    /// <summary>
    /// _productFolder'i BuildOrUpdate ile tarar/embed eder, kaydeder, UI'yi
    /// günceller ve freshness zaman damgasini yeniler. Hem manuel "İndeksi
    /// Güncelle" hem de arama öncesi otomatik güncelleme bunu kullanır.
    ///
    /// [Faz 4C] ImageIndex'in kendisi loglamayi bilmez (dusuk coupling) -
    /// burada, BuildOrUpdate'in zaten dondurdugu IndexUpdateStats/Issues
    /// verisinden log satirlari uretiliyor. Ana UI'da gosterilen ozet metni
    /// SADE tutulur (sifir sayimlar gizlenir); tum sayaclar log'da tam
    /// olarak kaliyor (bkz. BuildSummaryText / IndexScan log satiri).
    /// </summary>
    /// <summary>
    /// _productFolder'i, single-writer exclusive lock altinda BuildOrUpdate ile
    /// tarar/embed eder, kaydeder, UI'yi gunceller ve freshness zaman
    /// damgasini yeniler. Hem manuel "İndeksi Güncelle" hem de arama öncesi
    /// otomatik güncelleme bunu kullanır. Donus degeri: aramanin devam
    /// edebilecegi kullanilabilir (bellekte, entries&gt;0) bir index olup
    /// olmadigi - "basarili tarama oldu mu" ile AYNI SEY DEGIL (orn. lock
    /// alinamadi ama eski stable index hala kullanilabilir olabilir).
    /// </summary>
    private async Task<bool> RunIndexUpdateAsync(string trigger)
    {
        try
        {
            var folder = _productFolder!;
            var embedder = _embedder!;
            var wasFirstCreation = _indexEntries.Count == 0;
            var progress = new Progress<(int Done, int Total)>(p =>
                SetIndexStatus($"İndeksleniyor... {p.Done}/{p.Total}"));

            _logger.Info("IndexScan", reason: $"trigger={trigger} başladı");
            var result = await Task.Run(
                () => ImageIndex.BuildOrUpdateWithLock(folder, embedder, progress, _logger));

            switch (result.Outcome)
            {
                case IndexWriteOutcome.LockUnavailable:
                {
                    const string lockMessage = "İndeks şu anda başka bir kullanıcı tarafından güncelleniyor.\n"
                        + "Lütfen işlem tamamlandıktan sonra tekrar deneyin.";
                    _logger.Warning("IndexLock", file: folder,
                        reason: $"trigger={trigger}: kilit alınamadı" + (result.Failure is not null ? $" ({result.Failure.Message})" : string.Empty));

                    if (result.Failure is not null)
                    {
                        // Kilit "baskasi tutuyor" degil, .lens klasorune/erisime
                        // dair FARKLI bir sorun (izin, ag) - ayri, daha dogru mesaj.
                        SetIndexStatus($"İndeks klasörüne (.lens) erişilemedi: {result.Failure.Message}", success: false);
                        if (trigger == "Manual")
                        {
                            AlertWindow.Show(this, $"İndeks klasörüne (.lens) erişilemedi:\n{result.Failure.Message}",
                                "Erişim hatası", AlertKind.Warning);
                        }
                    }
                    else
                    {
                        SetIndexStatus(lockMessage, success: false);
                        if (trigger == "Manual")
                        {
                            AlertWindow.Show(this, lockMessage, "İndeks kilitli", AlertKind.Warning);
                        }
                    }

                    // Kilit alinamadiginda hicbir scan/save baslamadi - bellekteki
                    // (varsa) stable index DOKUNULMADAN kalir, arama onunla devam edebilir.
                    return _indexEntries.Count > 0;
                }

                case IndexWriteOutcome.ScanFailed:
                {
                    var scanError = result.Stats?.ScanError ?? "bilinmeyen hata";
                    SetIndexStatus($"Klasör taranamadı: {scanError}", success: false);
                    _logger.Error("IndexScan", file: folder, reason: $"trigger={trigger}: {scanError}");
                    if (trigger == "Manual")
                    {
                        AlertWindow.Show(this,
                            $"Ürün klasörü şu anda taranamadı (ör. ağ bağlantısı):\n{scanError}\n"
                            + "Mevcut kayıtlı index değiştirilmedi.",
                            "Tarama başarısız", AlertKind.Warning);
                    }

                    return _indexEntries.Count > 0;
                }

                case IndexWriteOutcome.SaveFailed:
                {
                    // [Network safety] entries burada YENİ (hesaplanmış ama
                    // kaydedilememiş) liste - _indexEntries'e BİLEREK atanmıyor:
                    // onceki guvenilir in-memory index korunur, UI/disk state'i
                    // celiskili "guncel" gorunmesin diye sahte basari da gosterilmez.
                    var saveError = result.Failure?.Message ?? "bilinmeyen hata";
                    SetIndexStatus($"İndeks paylaşılan klasöre kaydedilemedi: {saveError}\nÖnceki kayıtlı index korunuyor.", success: false);
                    _logger.Error("IndexSave", file: folder, reason: $"trigger={trigger}: {saveError}");
                    AlertWindow.Show(this,
                        $"İndeks paylaşılan klasöre kaydedilemedi:\n{saveError}\nÖnceki kayıtlı index korunuyor (bozulmadı).",
                        "Kaydetme başarısız", AlertKind.Warning);
                    return _indexEntries.Count > 0;
                }

                case IndexWriteOutcome.Updated:
                {
                    var entries = result.Entries;
                    var stats = result.Stats!;

                    _indexEntries = entries;
                    _lastFreshnessCheckUtc = DateTime.UtcNow;
                    _lastIssues = stats.Issues;
                    _lastSuccessfulStats = stats;
                    UpdateProblemFilesUi();
                    UpdateStatsUi();

                    ProductCountText.Text = $"{entries.Count} ürün";
                    var summaryText = BuildSummaryText(stats, entries.Count, wasFirstCreation);
                    var hasProblems = stats.FailedCount + stats.UnsupportedFormatCount + stats.SkippedNonImageCount > 0;
                    SetIndexStatus(summaryText, success: !hasProblems);

                    _logger.Info("IndexScan",
                        reason: $"trigger={trigger} total={stats.TotalFilesScanned} supported={stats.SupportedImagesSeen} "
                            + $"added={stats.Added} updated={stats.Updated} unchanged={stats.Unchanged} removed={stats.Removed} "
                            + $"failed={stats.FailedCount} unsupported={stats.UnsupportedFormatCount} skipped={stats.SkippedNonImageCount}");

                    foreach (var issue in stats.Issues)
                    {
                        // NonImageFile/UnsupportedImageFormat uygulama hatasi degildir
                        // (WARNING); yalnizca gercekten decode edilmeye calisilip
                        // basarisiz olan SupportedImageButFailed ERROR'dur.
                        var (operation, level) = issue.Kind switch
                        {
                            FileIssueKind.UnsupportedImageFormat => ("UnsupportedFormat", LogLevel.Warning),
                            FileIssueKind.NonImageFile => ("UnsupportedFile", LogLevel.Warning),
                            _ => ("IndexingFailed", LogLevel.Error),
                        };

                        if (level == LogLevel.Warning)
                        {
                            _logger.Warning(operation, file: issue.FileName, extension: issue.Extension, reason: issue.Reason);
                        }
                        else
                        {
                            _logger.Error(operation, file: issue.FileName, extension: issue.Extension, reason: issue.Reason);
                        }
                    }

                    return true;
                }

                default:
                    return _indexEntries.Count > 0;
            }
        }
        catch (Exception ex)
        {
            SetIndexStatus("İndeksleme başarısız oldu.", success: false);
            _logger.Error("IndexScan", reason: $"trigger={trigger}: {ex.Message}");
            AlertWindow.Show(this, $"İndeksleme sırasında hata oluştu:\n{ex.Message}", "Hata", AlertKind.Error);
            return _indexEntries.Count > 0;
        }
    }

    /// <summary>
    /// [Faz 1] "Ara" oncesi index'in hazir olup olmadigini, auto-index
    /// checkbox tercihine gore saglar. Kapaliyken hicbir scan/write yapmaz -
    /// yalnizca bellekteki mevcut stable shared index'i kullanir. Aciksa
    /// index yok/bos ise olusturur, TTL dolmussa DetectChanges/BuildOrUpdate
    /// calistirir. Donus degeri: aramanin baslayip baslamayacagi.
    /// </summary>
    private async Task<bool> EnsureIndexReadyForSearchAsync()
    {
        var folder = _productFolder!;
        var autoIndex = AutoIndexCheckBox.IsChecked == true;

        if (!autoIndex)
        {
            if (_indexEntries.Count == 0)
            {
                SetIndexStatus("Kullanılabilir bir indeks bulunamadı. Lütfen 'İndeksi Güncelle / Klasörü Tara' butonunu kullanın.", success: false);
                AlertWindow.Show(this,
                    "Kullanılabilir bir indeks bulunamadı. Lütfen 'İndeksi Güncelle / Klasörü Tara' butonunu kullanın.",
                    "Index yok", AlertKind.Warning);
                return false;
            }

            return true;
        }

        if (_indexEntries.Count == 0)
        {
            SetIndexStatus("İndeks bulunamadı, oluşturuluyor...");
            var created = await RunIndexUpdateAsync(trigger: "AutoCreate");
            return created && _indexEntries.Count > 0;
        }

        var now = DateTime.UtcNow;
        if (_lastFreshnessCheckUtc is null || now - _lastFreshnessCheckUtc >= FreshnessCheckInterval)
        {
            SetIndexStatus("Klasör güncelliği kontrol ediliyor...");
            var changes = await Task.Run(() => ImageIndex.DetectChanges(folder, _logger));

            if (changes.ScanError is not null)
            {
                SetIndexStatus(
                    $"Klasör güncelliği kontrol edilemedi ({changes.ScanError}). Kayıtlı index ile aranıyor...",
                    success: false);
                _logger.Warning("FreshnessCheck", file: folder, reason: changes.ScanError);
                // Ag gecici olarak erisilemez olabilir - kullaniciyi tamamen
                // durdurmuyoruz, elimizdeki son bilinen index ile arama
                // yapmaya devam ediyoruz.
            }
            else if (changes.HasChanges)
            {
                SetIndexStatus(
                    $"Değişiklik bulundu (yeni={changes.NewCount}, değişen={changes.ChangedCount}, "
                    + $"silinen={changes.RemovedCount}). İndeksleniyor...");
                _logger.Info("FreshnessCheck",
                    reason: $"new={changes.NewCount} changed={changes.ChangedCount} removed={changes.RemovedCount}");
                await RunIndexUpdateAsync(trigger: "AutoFreshness");
            }
            else
            {
                _logger.Info("FreshnessCheck", reason: "değişiklik yok");
                _lastFreshnessCheckUtc = now;
            }
        }

        return _indexEntries.Count > 0;
    }

    /// <summary>
    /// [Faz 4C] Ana ekranda gösterilen özet metni SADE tutar: sıfır sayımlar
    /// atlanır, "değişmeyen" hiç gösterilmez (kullanıcı için anlamlı değil).
    /// Tüm ayrıntı (failed/unsupported ayrımı dahil) log dosyasında ve
    /// "Sorunlu / Atlanan Dosyalar" penceresinde eksiksiz kalır - burada yalnızca
    /// gösterim metni sadeleştiriliyor, IndexUpdateStats'ın kendisi değil.
    /// </summary>
    private static string BuildSummaryText(IndexUpdateStats stats, int totalEntries, bool isFirstCreation)
    {
        // [Kullanici geri bildirimi] Gorsel-olmayan/desteklenmeyen dosyalar
        // (.pdf/.zip vb.) artik sessizce yok sayilmiyor - "sorun" sayacina
        // dahil edilir ki ana ozette de gorunsun. Detay: Sorunlu/Atlanan
        // Dosyalar penceresi.
        var problemCount = stats.FailedCount + stats.UnsupportedFormatCount + stats.SkippedNonImageCount;

        if (isFirstCreation)
        {
            var created = $"İndeks oluşturuldu — {totalEntries:N0} ürün hazır";
            if (problemCount > 0)
            {
                created += $", {problemCount} sorun bulundu";
            }

            return created + ".";
        }

        var parts = new List<string>();
        if (stats.Added > 0)
        {
            parts.Add($"{stats.Added} yeni");
        }

        if (stats.Updated > 0)
        {
            parts.Add($"{stats.Updated} güncellenen");
        }

        if (stats.Removed > 0)
        {
            parts.Add($"{stats.Removed} silinen");
        }

        if (parts.Count == 0)
        {
            return problemCount > 0
                ? $"İndeks güncel — değişiklik bulunmadı, {problemCount} sorun bulundu."
                : "İndeks güncel — değişiklik bulunmadı.";
        }

        var summary = "İndeks güncellendi — " + string.Join(", ", parts);
        if (problemCount > 0)
        {
            summary += $", {problemCount} sorun bulundu";
        }

        return summary + ".";
    }

    /// <summary>
    /// [Faz 2] Ana UI'daki ayrıntılı sayaçları gösterir - sıfır değerler dahil
    /// (manager tam özet ister, bkz. proje talimatı). Yalnızca SON BAŞARILI
    /// taramanın (_lastSuccessfulStats) sayıları gösterilir ve panel bunu
    /// AÇIKÇA "son başarılı tarama" olarak etiketler - başarısız bir tarama
    /// (lock/scan/save hatası) bu paneli SIFIRLAMAZ/YANILTMAZ, çünkü
    /// _lastSuccessfulStats yalnızca IndexWriteOutcome.Updated durumunda
    /// güncellenir (bkz. RunIndexUpdateAsync). Manuel ve otomatik güncelleme
    /// AYNI yolu (RunIndexUpdateAsync -> burası) kullanır.
    /// </summary>
    private void UpdateStatsUi()
    {
        if (_lastSuccessfulStats is null)
        {
            DetailedStatsText.Text = "Henüz başarılı bir tarama yapılmadı.";
            return;
        }

        var s = _lastSuccessfulStats;
        DetailedStatsText.Text =
            $"Son başarılı tarama — Yeni: {s.Added}   Güncellenen: {s.Updated}   Değişmeyen: {s.Unchanged}   "
            + $"Silinen: {s.Removed}   Okunamayan: {s.FailedCount}   Desteklenmeyen görsel: {s.UnsupportedFormatCount}   "
            + $"Desteklenmeyen dosya: {s.SkippedNonImageCount}";
    }

    /// <summary>[Faz 4C] "Sorunlu / Atlanan Dosyalar (N)" butonunu son sonuca göre günceller; sorun yoksa gizler.</summary>
    private void UpdateProblemFilesUi()
    {
        if (_lastIssues.Count > 0)
        {
            ProblemFilesButton.Content = $"Sorunlu / Atlanan Dosyalar ({_lastIssues.Count})";
            ProblemFilesButton.Visibility = Visibility.Visible;
        }
        else
        {
            ProblemFilesButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ProblemFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProblemFilesWindow(_lastIssues) { Owner = this };
        window.ShowDialog();
    }

    /// <summary>
    /// [Faz 4D polish] "Geri" degil, "Yeni Arama": sorgu/karsilastirma/Top-10
    /// durumunu temizler ama urun klasoru, index ve cache'e dokunmaz -
    /// kullanici tekrar klasor secmek zorunda kalmaz.
    /// </summary>
    private void NewSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _queryImagePath = null;
        QueryPreviewImage.Source = null;
        QueryFileNameText.Text = string.Empty;
        _results.Clear();
        ClearComparison();
        ResetResultsScroll();
    }

    private void SelectQueryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sorgu Görseli Seçin",
            Filter = "Görsel Dosyaları (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        LoadQueryImage(dialog.FileName);
    }

    /// <summary>
    /// [Faz 4D] "Sorgu Görseli Seç" butonu ve drag&amp;drop icin ortak yukleme
    /// yolu - onizlemeyi gunceller, eski arama/karsilastirma sonuclarini
    /// temizler. Bozuk-ama-dogru-uzantili bir dosya (ornegin gercekte gorsel
    /// olmayan bir .jpg) burada yakalanir, uygulama cokmez.
    /// </summary>
    private void LoadQueryImage(string path)
    {
        try
        {
            // [Hard limit kaldirildi] Buyuk/asiri yuksek cozunurluklu gorseller
            // artik REDDEDILMIYOR - LoadPreview zaten DecodePixelWidth=300 ile
            // ekonomik (kucuk) bir onizleme decode eder, boyuttan bagimsiz
            // ucuzdur. Asil embed (CLIP) asamasindaki ekonomik decode icin
            // bkz. ImagePreprocessor.LoadForPreprocessing.
            QueryPreviewImage.Source = LoadPreview(path);
            _queryImagePath = path;
            QueryFileNameText.Text = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            _queryImagePath = null;
            QueryPreviewImage.Source = null;
            QueryFileNameText.Text = string.Empty;
            AlertWindow.Show(this, $"Görsel önizlemesi yüklenemedi:\n{ex.Message}", "Görsel okunamadı", AlertKind.Error);
        }

        _results.Clear();
        ClearComparison();
        ResetResultsScroll();
    }

    private void QueryDropZone_DragEnter(object sender, DragEventArgs e)
    {
        var isValid = TryGetDroppedImagePath(e.Data, out var path, out _);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        // [Faz 4D polish] Gecersiz surukleme icin yanlis "kabul edilebilir"
        // gorunumu vermeyelim - vurgu yalnizca gercekten kabul edilecek bir
        // dosya oldugunda gosterilir.
        SetQueryDropZoneActive(isValid);

        RemoveDragPreview();
        if (isValid)
        {
            // Surukleme sirasinda kucuk/ucuz bir onizleme yuklenir (64px) -
            // Drop'ta LoadQueryImage'in yaptigi tam onizlemeden ayri ve
            // DragOver'da TEKRAR yuklenmez (yalnizca pozisyon guncellenir).
            var thumbnail = TryLoadDragThumbnail(path!);
            if (thumbnail is not null)
            {
                ShowDragPreview(thumbnail, e.GetPosition(RootGrid));
            }
        }

        e.Handled = true;
    }

    private void QueryDropZone_DragOver(object sender, DragEventArgs e)
    {
        var isValid = TryGetDroppedImagePath(e.Data, out _, out _);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        _dragPreviewAdorner?.UpdatePosition(e.GetPosition(RootGrid));
        e.Handled = true;
    }

    private void QueryDropZone_DragLeave(object sender, DragEventArgs e)
    {
        SetQueryDropZoneActive(false);
        RemoveDragPreview();
    }

    private void QueryDropZone_Drop(object sender, DragEventArgs e)
    {
        SetQueryDropZoneActive(false);
        RemoveDragPreview();

        if (!TryGetDroppedImagePath(e.Data, out var path, out var error))
        {
            AlertWindow.Show(this, error, "Sürükle-bırak", AlertKind.Warning);
            return;
        }

        LoadQueryImage(path!);
    }

    private static BitmapImage? TryLoadDragThumbnail(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 64;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Surukleme sirasinda dosya gecici olarak kilitli/erisilemez
            // olabilir - onizleme sadece atlanir, surukleme islemi bozulmaz.
            return null;
        }
    }

    /// <summary>
    /// [Faz 4D polish] Windows Explorer'in surukleme sirasinda gosterdigi
    /// "ghost" gorsel, fare Lens penceresine girince kayboluyor (ayri
    /// process/pencere). Bunu telafi etmek icin AdornerLayer uzerinde sade,
    /// yari saydam bir onizleme fareyi takip eder. Adorner IsHitTestVisible=
    /// false ile isaretlenir ki WPF'in drag-event hit-testi QueryDropZone'a
    /// degil yanlislikla adorner'a gitmesin (drop davranisini bozmaz).
    /// </summary>
    private void ShowDragPreview(BitmapImage thumbnail, Point position)
    {
        var layer = AdornerLayer.GetAdornerLayer(RootGrid);
        if (layer is null)
        {
            return;
        }

        _dragPreviewAdorner = new DragPreviewAdorner(RootGrid, thumbnail);
        _dragPreviewAdorner.UpdatePosition(position);
        layer.Add(_dragPreviewAdorner);
    }

    private void RemoveDragPreview()
    {
        if (_dragPreviewAdorner is null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(RootGrid);
        layer?.Remove(_dragPreviewAdorner);
        _dragPreviewAdorner = null;
    }

    /// <summary>
    /// [Faz 4D polish] Surukle-birak sirasinda sade bir "buraya birakilabilir"
    /// geri bildirimi. Animasyon/glow yok - yalnizca border/arka plan/ipucu
    /// metni degisimi. false ile cagrildiginda panel, her zaman gorunen
    /// "sorgulanan gorsel" vurgusuna (accent border, 2px) doner.
    /// </summary>
    private void SetQueryDropZoneActive(bool active)
    {
        if (active)
        {
            // Surukleme sirasinda GECICI accent vurgusu - normal durumda
            // query paneli asla mavi olmaz (bkz. NeutralBorderBrush).
            QueryDropZone.BorderBrush = (Brush)FindResource("AccentBrush");
            QueryDropZone.BorderThickness = new Thickness(3);
            QueryDropZone.Background = (Brush)FindResource("AccentBrushLight");
            QueryDropHintText.Text = "Görseli buraya bırak";
        }
        else
        {
            QueryDropZone.BorderBrush = (Brush)FindResource("NeutralBorderBrush");
            QueryDropZone.BorderThickness = new Thickness(2);
            QueryDropZone.Background = Brushes.White;
            QueryDropHintText.Text = "Görsel seçin veya buraya sürükleyin  •  çift tık: büyüt";
        }
    }

    /// <summary>
    /// [Faz 4D] Tek dosya + desteklenen gorsel formati kontrolu. Faz 4B'nin
    /// FileClassifier'i yeniden kullanilir - ayrica bir uzanti listesi
    /// tutulmaz.
    /// </summary>
    private static bool TryGetDroppedImagePath(IDataObject data, out string? path, out string error)
    {
        path = null;
        error = string.Empty;

        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            error = "Yalnızca dosya sürükleyip bırakabilirsiniz.";
            return false;
        }

        var files = (string[])data.GetData(DataFormats.FileDrop)!;
        if (files.Length != 1)
        {
            error = "Lütfen tek bir görsel dosyası bırakın.";
            return false;
        }

        var file = files[0];
        if (Directory.Exists(file))
        {
            error = "Klasör bırakılamaz, lütfen bir görsel dosyası seçin.";
            return false;
        }

        if (FileClassifier.Classify(Path.GetExtension(file)) != FileClassification.SupportedImage)
        {
            error = "Desteklenmeyen dosya formatı. Lütfen jpg/jpeg/png seçin.";
            return false;
        }

        path = file;
        return true;
    }

    /// <summary>[Faz 4D] Top-10 kartlarindan birine tiklandiginda karsilastirma panelini gunceller.</summary>
    private void ResultCard_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SearchResultViewModel vm)
        {
            SelectResult(vm);
        }
    }

    /// <summary>[Faz 4D polish] Cift tik: ayni karin tek-tik secimini bozmadan buyuk onizleme acar.</summary>
    private void ResultCard_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SearchResultViewModel vm)
        {
            TryOpenImagePreview(vm.FullPath);
        }
    }

    private void QueryDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            TryOpenImagePreview(_queryImagePath);
        }
    }

    private void ComparisonResultBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            TryOpenImagePreview(_selectedResult?.FullPath);
        }
    }

    /// <summary>
    /// [Faz 4D polish] Buyuk onizleme icin dosyayi TAM cozunurlukte yeniden
    /// okur (thumbnail'lar 300px'e sinirli - detay incelemeye yetmez).
    /// Dosya silinmis/erisilemez olabilir (UNC ag klasoru) - basarisizlik
    /// sadece bir uyari gosterir, MainWindow'u etkilemez.
    /// </summary>
    /// <summary>[Hard limit kaldirildi] Buyuk onizlemeler icin makul bir ust decode genisligi - ekran/zoom kalitesini pratikte etkilemez, sadece asiri buyuk dosyalarda bellek/donma riskini azaltir. Tek, kolay degistirilebilir sabit.</summary>
    private const int MaxPreviewDecodePixelWidth = 4096;

    private void TryOpenImagePreview(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);

            // [Hard limit kaldirildi - kesin product karari] Asiri buyuk/yuksek
            // cozunurluklu gorseller artik REDDEDILMIYOR. Onceden burada tam
            // cozunurlukte decode edilip limit asilirsa reddediliyordu; simdi
            // bunun yerine yalnizca esigin USTUNDEKI dosyalar icin ekonomik
            // (bounded) bir decode genisligi uygulanir - kucuk/normal gorseller
            // (esigin altinda) ONCEKI ile BIREBIR AYNI (tam cozunurluk) yolu kullanir.
            if (ImageResourceLimits.TryGetPixelCount(path) > ImageResourceLimits.LargeImagePixelHint)
            {
                bitmap.DecodePixelWidth = MaxPreviewDecodePixelWidth;
            }

            bitmap.EndInit();
            bitmap.Freeze();

            // [Faz 4D polish - kullanici geri bildirimi] Onizlemeler ekranda
            // birikmesin: yenisi acilmadan once acik olan onceki onizleme kapatilir.
            _openPreview?.Close();

            var preview = new ImagePreviewWindow(bitmap, Path.GetFileName(path)) { Owner = this };
            _openPreview = preview;
            preview.Closed += (_, _) =>
            {
                if (ReferenceEquals(_openPreview, preview))
                {
                    _openPreview = null;
                }
            };
            preview.Show();
        }
        catch (Exception ex)
        {
            AlertWindow.Show(this, $"Görsel açılamadı (dosya silinmiş veya erişilemez olabilir):\n{ex.Message}",
                "Önizleme açılamadı", AlertKind.Warning);
            _logger.Warning("ImagePreview", file: path, reason: ex.Message);
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MenuButton;
            menu.IsOpen = true;
        }
    }

    /// <summary>
    /// [Faz 4D polish] Ayarlar bilinçli olarak SALT-OKUNUR bir ozet: Faz 4A'nin
    /// admin-default/user-override mimarisini/UI'ini degistirmiyoruz - klasor
    /// degistirme/varsayilan yapma islemleri hala ana ekrandaki mevcut
    /// butonlarla yapiliyor (menude tekrarlanmiyor, state senkronizasyonu
    /// karmasikligi eklemeye gerek yok).
    /// </summary>
    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var folder = _productFolder ?? "(seçilmedi)";
        var source = string.IsNullOrEmpty(DirectorySourceText.Text) ? "(yok)" : DirectorySourceText.Text;
        var message =
            $"Ürün klasörü: {folder}\n" +
            $"Kaynak: {source}\n\n" +
            $"Yönetici config dosyası:\n{AppPaths.AdminConfigFilePath}\n\n" +
            $"Kullanıcı ayarları dosyası:\n{AppPaths.UserSettingsFilePath}\n\n" +
            "Klasörü değiştirmek veya varsayılan yapmak için ana ekrandaki "
            + "\"Ürün Klasörü Seç\", \"Bu Klasörü Varsayılan Yap\" ve "
            + "\"Varsayılanı Temizle\" butonlarını kullanın.";
        AlertWindow.Show(this, message, "Ayarlar", AlertKind.Information);
    }

    private void OpenLogFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureLocalDirectoriesExist();
            Process.Start(new ProcessStartInfo { FileName = AppPaths.LogsDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AlertWindow.Show(this, $"Log klasörü açılamadı:\n{ex.Message}", "Klasör açılamadı", AlertKind.Warning);
            _logger.Warning("OpenLogFolder", reason: ex.Message);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "MVP" : $"{version.Major}.{version.Minor}.{version.Build}";
        var message = $"Lens\nGörsel Ürün Arama Sistemi\nSürüm: {versionText}";
        AlertWindow.Show(this, message, "Hakkında", AlertKind.Information);
    }

    private void SelectResult(SearchResultViewModel result)
    {
        foreach (var r in _results)
        {
            r.IsSelected = ReferenceEquals(r, result);
        }

        _selectedResult = result;
        ComparisonResultImage.Source = result.Thumbnail;
        ComparisonFileNameText.Text = result.FileName;
        ComparisonScoreText.Text = result.ScoreText;
        // [Faz 4D polish] Yalnizca goruntulenen deger tam %100 oldugunda
        // basari/yesil vurgusu - diger skorlar notr kalir. SuccessBrush/NeutralTextBrush
        // artik tema-bagimli (bkz. SetTheme) - ayrica bir "OnDark..." varyanti gerekmez.
        ComparisonScoreText.Foreground = result.IsPerfectMatch
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("NeutralTextBrush");
    }

    private void ClearComparison()
    {
        foreach (var r in _results)
        {
            r.IsSelected = false;
        }

        _selectedResult = null;
        ComparisonResultImage.Source = null;
        ComparisonFileNameText.Text = string.Empty;
        ComparisonScoreText.Text = string.Empty;
        ComparisonScoreText.Foreground = (Brush)FindResource("NeutralTextBrush");
    }

    /// <summary>
    /// [Faz 1] Siralama: 1) urun klasoru, 2) sorgu gorseli, 3) threshold
    /// validasyonu (pahali islemlerden ONCE), 4) model hazirligi, 5) auto-index
    /// tercihine gore index hazirlama/kontrol, 6) kullanilabilir index
    /// kontrolu, 7) threshold filtreli en fazla 15 sonuclu arama.
    /// </summary>
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_productFolder is null)
        {
            AlertWindow.Show(this, "Önce bir ürün klasörü seçin.", "Klasör seçilmedi", AlertKind.Warning);
            return;
        }

        if (_queryImagePath is null)
        {
            AlertWindow.Show(this, "Önce bir sorgu görseli seçin.", "Görsel seçilmedi", AlertKind.Warning);
            return;
        }

        if (!SimilarityThreshold.TryParse(ThresholdTextBox.Text, out var thresholdPercent))
        {
            ShowThresholdValidationError();
            return;
        }

        HideThresholdValidationError();

        if (!TryEnsureEmbedder(out var modelError))
        {
            AlertWindow.Show(this, modelError, "Model yüklenemedi", AlertKind.Error);
            return;
        }

        SetBusy(true);
        try
        {
            var ready = await EnsureIndexReadyForSearchAsync();
            if (!ready)
            {
                // Kullanicidan aksiyon isteyen uygun mesaj EnsureIndexReadyForSearchAsync
                // icinde zaten gosterildi - burada sessizce durulur.
                return;
            }

            SetIndexStatus("Aranıyor...");
            var searchStopwatch = Stopwatch.StartNew();

            var queryPath = _queryImagePath;
            var entries = _indexEntries;
            var embedder = _embedder!;

            var matches = await Task.Run(() =>
            {
                var embedding = embedder.Embed(queryPath);
                return SimilaritySearch.SearchWithThreshold(embedding, entries, thresholdPercent);
            });

            _results.Clear();
            foreach (var r in matches)
            {
                var fullPath = Path.Combine(_productFolder, r.RelativePath);
                var scoreText = $"Benzerlik: {r.Score:P1}";
                _results.Add(new SearchResultViewModel
                {
                    FileName = r.RelativePath,
                    ScoreText = scoreText,
                    Thumbnail = TryLoadPreview(fullPath),
                    FullPath = fullPath,
                    IsPerfectMatch = scoreText.EndsWith("100.0%", StringComparison.Ordinal),
                });
            }

            searchStopwatch.Stop();
            // [Scroll fix] Her GERCEK yeni arama (esik degisimi/ayni sorgu tekrar dahil)
            // sonuc listesini en basa dondurur - eski kaydirma konumu bir sonraki
            // aramaya TASINMAZ. Validasyon hatasinda (yukarida erken return) bu satira
            // hic ulasilmaz, dolayisiyla gecersiz girdi mevcut ekrani kaydirmaz.
            ResetResultsScroll();

            if (_results.Count > 0)
            {
                // [Faz 4D] Karsilastirma alani hicbir zaman bos kalmasin diye
                // ilk (en yuksek skorlu) sonuc otomatik secilir; kullanici
                // isterse listeden baskasina gecer.
                SelectResult(_results[0]);
                SetIndexStatus($"{matches.Count} sonuç bulundu.", success: true);
            }
            else
            {
                // [Faz 1] No-result HATA DEGILDIR: onceki results/selection
                // temizlenir, query gorseli ve threshold girdisi KORUNUR,
                // modal gosterilmez - kullanici threshold'u degistirip
                // tekrar arayabilir.
                ClearComparison();
                SetIndexStatus("Seçilen minimum benzerlik değerini karşılayan sonuç bulunamadı.");
            }

            _logger.Info("Search", file: Path.GetFileName(queryPath),
                reason: $"results={matches.Count} threshold={thresholdPercent} duration_ms={searchStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            SetIndexStatus("Arama başarısız oldu.", success: false);
            _logger.Error("Search", file: _queryImagePath, reason: ex.Message);
            AlertWindow.Show(this, $"Arama sırasında hata oluştu:\n{ex.Message}", "Hata", AlertKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>[Faz 1] Gecersiz threshold: odak hatali alana doner, sade (modal olmayan) bir mesaj gosterilir.</summary>
    private void ShowThresholdValidationError()
    {
        ThresholdValidationText.Text = "Lütfen 0-100 arasında geçerli bir minimum benzerlik yüzdesi girin.";
        ThresholdValidationText.Visibility = Visibility.Visible;
        ThresholdTextBox.Focus();
        ThresholdTextBox.SelectAll();
    }

    private void HideThresholdValidationError()
    {
        ThresholdValidationText.Visibility = Visibility.Collapsed;
    }

    private bool TryEnsureEmbedder(out string error)
    {
        error = string.Empty;
        if (_embedder is not null)
        {
            return true;
        }

        var modelPath = ResolveModelPath();
        if (modelPath is null)
        {
            error = "CLIP ONNX model dosyası bulunamadı (models\\clip-vision-b16-openai.onnx). "
                  + "Model dosyasının uygulama klasöründeki 'models' alt klasöründe olduğundan emin olun.";
            return false;
        }

        try
        {
            _embedder = new ClipEmbedder(modelPath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Model yüklenirken hata oluştu:\n{ex.Message}";
            return false;
        }
    }

    private static string? ResolveModelPath()
    {
        var nextToExe = Path.Combine(AppContext.BaseDirectory, "models", "clip-vision-b16-openai.onnx");
        if (File.Exists(nextToExe))
        {
            return nextToExe;
        }

        // Gelistirme ortaminda (dotnet run, model henuz output'a kopyalanmadan)
        // repo kokune kadar yukari cikip models/ altina bak.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lens.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        var repoCandidate = Path.Combine(dir.FullName, "models", "clip-vision-b16-openai.onnx");
        return File.Exists(repoCandidate) ? repoCandidate : null;
    }

    private static BitmapImage LoadPreview(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.DecodePixelWidth = 300;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage? TryLoadPreview(string path)
    {
        try
        {
            return LoadPreview(path);
        }
        catch
        {
            return null;
        }
    }

    private void SetBusy(bool busy)
    {
        SelectFolderButton.IsEnabled = !busy;
        UpdateIndexButton.IsEnabled = !busy;
        SelectQueryButton.IsEnabled = !busy;
        SearchButton.IsEnabled = !busy;
        ThresholdTextBox.IsEnabled = !busy;
        AutoIndexCheckBox.IsEnabled = !busy;
        SetDefaultButton.IsEnabled = !busy && _productFolder is not null && _directoryOrigin != DirectoryOrigin.UserOverride;
        ClearDefaultButton.IsEnabled = !busy;
        ProblemFilesButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _embedder?.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>[Faz 4D] IsSelected, Top-10 kartlarindan hangisinin karsilastirma panelinde gosterildigini XAML'e (accent border) bildirir.</summary>
public sealed class SearchResultViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string FileName { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public BitmapImage? Thumbnail { get; set; }

    /// <summary>[Faz 4D polish] Buyuk onizleme icin diskten tam cozunurlukte yeniden okunacak dosya yolu.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// [Faz 4D polish] ScoreText ile AYNI bicimlendirmeden (P1) turetilir -
    /// UI'da gosterilen yuvarlanmis deger ile tutarli olmasi icin ham double
    /// karsilastirmasi (== 1.0) yerine bicimlendirilmis metnin kendisi kontrol
    /// edilir.
    /// </summary>
    public bool IsPerfectMatch { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// [Faz 4D polish] Surukleme sirasinda fareyi takip eden sade, yari saydam
/// bir onizleme. IsHitTestVisible=false - WPF'in drag-event hit-testini
/// (dolayisiyla DragOver/Drop davranisini) etkilemez.
/// </summary>
internal sealed class DragPreviewAdorner : Adorner
{
    private readonly Image _image;
    private Point _position;

    public DragPreviewAdorner(UIElement adornedElement, ImageSource source) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _image = new Image
        {
            Source = source,
            Width = 64,
            Height = 64,
            Stretch = Stretch.Uniform,
            Opacity = 0.75,
            IsHitTestVisible = false,
        };
        AddVisualChild(_image);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _image;

    public void UpdatePosition(Point position)
    {
        _position = position;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _image.Measure(new Size(_image.Width, _image.Height));
        return base.MeasureOverride(constraint);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Kucuk bir ofset ile OS'un sürükleme "ghost"una benzer sekilde
        // imlecin hemen sag-altina yerlesir, imleci kapatmaz.
        _image.Arrange(new Rect(_position.X + 14, _position.Y + 14, _image.Width, _image.Height));
        return finalSize;
    }
}
