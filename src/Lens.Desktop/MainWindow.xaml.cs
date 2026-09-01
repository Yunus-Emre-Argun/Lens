using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    public MainWindow()
    {
        InitializeComponent();
        ResultsItemsControl.ItemsSource = _results;
        _logger.Info("AppStart");
        LoadDefaultProductDirectory();
    }

    /// <summary>
    /// [Faz 4D] Ana ekrandaki durum metnini, sonucun niteligine gore hafif bir
    /// renk vurgusuyla gosterir (success=yesil, warning/hata=kirmizimsi,
    /// null=notr). Salt UI vurgusu - IndexUpdateStats/log icerigini etkilemez.
    /// </summary>
    private void SetIndexStatus(string text, bool? success = null)
    {
        IndexStatusText.Text = text;
        IndexStatusText.Foreground = success switch
        {
            true => (Brush)FindResource("SuccessBrush"),
            false => (Brush)FindResource("WarningBrush"),
            null => (Brush)FindResource("NeutralTextBrush"),
        };
    }

    /// <summary>
    /// [Faz 4A] Acilista admin default / kullanici override'i coz, erisilebilirse
    /// otomatik yukle. Indeksleme burada TETIKLENMEZ - klasor hazir gelir,
    /// kullanici "Indeksi Guncelle" ile taramayi kendisi baslatir.
    /// </summary>
    private void LoadDefaultProductDirectory()
    {
        var resolution = ProductDirectoryResolver.ResolveDefault(_logger);

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
        _indexEntries = ImageIndex.Load(_productFolder);
        _lastFreshnessCheckUtc = null;
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        SetIndexStatus(_indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : "Varsayılan klasör yüklendi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.");

        _directoryOrigin = resolution.Source == ProductDirectorySource.UserOverride
            ? DirectoryOrigin.UserOverride
            : DirectoryOrigin.AdminDefault;
        UpdateDirectoryOriginUi();
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
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
        _lastFreshnessCheckUtc = null;

        _indexEntries = ImageIndex.Load(_productFolder);
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        SetIndexStatus(_indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
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
            MessageBox.Show(this, "Önce bir ürün klasörü seçin.", "Klasör seçilmedi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var hasSupportedImage = Directory.EnumerateFiles(_productFolder)
            .Any(f => FileClassifier.Classify(Path.GetExtension(f)) == FileClassification.SupportedImage);
        if (!hasSupportedImage)
        {
            MessageBox.Show(this, "Bu klasörde desteklenen görsel (jpg/jpeg/png) bulunamadı.",
                "Görsel bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryEnsureEmbedder(out var modelError))
        {
            MessageBox.Show(this, modelError, "Model yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusy(true);
        SetIndexStatus("İndeksleniyor...");

        // Manuel "İndeksi Güncelle" her zaman FORCE SCAN yapar (freshness
        // kontrolünü atlar). Bu, arama öncesi otomatik freshness-check'in
        // ("Ara") de aynı işlevi görecek olmasından bağımsızdır.
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
    private async Task RunIndexUpdateAsync(string trigger)
    {
        try
        {
            var folder = _productFolder!;
            var embedder = _embedder!;
            var wasFirstCreation = _indexEntries.Count == 0;
            var progress = new Progress<(int Done, int Total)>(p =>
                SetIndexStatus($"İndeksleniyor... {p.Done}/{p.Total}"));

            _logger.Info("IndexScan", reason: $"trigger={trigger} başladı");
            var (entries, stats) = await Task.Run(
                () => ImageIndex.BuildOrUpdate(folder, embedder, progress));

            if (stats.ScanError is not null)
            {
                SetIndexStatus($"Klasör taranamadı: {stats.ScanError}", success: false);
                _logger.Error("IndexScan", file: folder, reason: $"trigger={trigger}: {stats.ScanError}");
                MessageBox.Show(this,
                    $"Ürün klasörü şu anda taranamadı (ör. ağ bağlantısı):\n{stats.ScanError}\n"
                    + "Mevcut kayıtlı index değiştirilmedi.",
                    "Tarama başarısız", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _indexEntries = entries;
            ImageIndex.Save(folder, entries);
            _lastFreshnessCheckUtc = DateTime.UtcNow;
            _lastIssues = stats.Issues;
            UpdateProblemFilesUi();

            ProductCountText.Text = $"{entries.Count} ürün";
            var summaryText = BuildSummaryText(stats, entries.Count, wasFirstCreation);
            var hasProblems = stats.FailedCount + stats.UnsupportedFormatCount > 0;
            SetIndexStatus(summaryText, success: !hasProblems);

            _logger.Info("IndexScan",
                reason: $"trigger={trigger} total={stats.TotalFilesScanned} supported={stats.SupportedImagesSeen} "
                    + $"added={stats.Added} updated={stats.Updated} unchanged={stats.Unchanged} removed={stats.Removed} "
                    + $"failed={stats.FailedCount} unsupported={stats.UnsupportedFormatCount} skipped={stats.SkippedNonImageCount}");

            foreach (var issue in stats.Issues)
            {
                var operation = issue.Kind == FileIssueKind.UnsupportedImageFormat ? "UnsupportedFormat" : "IndexingFailed";
                var level = issue.Kind == FileIssueKind.UnsupportedImageFormat ? LogLevel.Warning : LogLevel.Error;
                if (level == LogLevel.Warning)
                {
                    _logger.Warning(operation, file: issue.FileName, extension: issue.Extension, reason: issue.Reason);
                }
                else
                {
                    _logger.Error(operation, file: issue.FileName, extension: issue.Extension, reason: issue.Reason);
                }
            }
        }
        catch (Exception ex)
        {
            SetIndexStatus("İndeksleme başarısız oldu.", success: false);
            _logger.Error("IndexScan", reason: $"trigger={trigger}: {ex.Message}");
            MessageBox.Show(this, $"İndeksleme sırasında hata oluştu:\n{ex.Message}",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// [Faz 4C] Ana ekranda gösterilen özet metni SADE tutar: sıfır sayımlar
    /// atlanır, "değişmeyen" hiç gösterilmez (kullanıcı için anlamlı değil).
    /// Tüm ayrıntı (failed/unsupported ayrımı dahil) log dosyasında ve
    /// "Sorunlu Dosyalar" penceresinde eksiksiz kalır - burada yalnızca
    /// gösterim metni sadeleştiriliyor, IndexUpdateStats'ın kendisi değil.
    /// </summary>
    private static string BuildSummaryText(IndexUpdateStats stats, int totalEntries, bool isFirstCreation)
    {
        var problemCount = stats.FailedCount + stats.UnsupportedFormatCount;

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

    /// <summary>[Faz 4C] "Sorunlu Dosyalar (N)" butonunu son sonuca göre günceller; sorun yoksa gizler.</summary>
    private void UpdateProblemFilesUi()
    {
        if (_lastIssues.Count > 0)
        {
            ProblemFilesButton.Content = $"Sorunlu Dosyalar ({_lastIssues.Count})";
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
        _results.Clear();
        ClearComparison();
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
            QueryPreviewImage.Source = LoadPreview(path);
            _queryImagePath = path;
        }
        catch (Exception ex)
        {
            _queryImagePath = null;
            QueryPreviewImage.Source = null;
            MessageBox.Show(this, $"Görsel önizlemesi yüklenemedi:\n{ex.Message}",
                "Görsel okunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _results.Clear();
        ClearComparison();
    }

    private void QueryDropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedImagePath(e.Data, out _, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void QueryDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedImagePath(e.Data, out var path, out var error))
        {
            MessageBox.Show(this, error, "Sürükle-bırak", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadQueryImage(path!);
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
            MessageBox.Show(this, $"Görsel açılamadı (dosya silinmiş veya erişilemez olabilir):\n{ex.Message}",
                "Önizleme açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        MessageBox.Show(this, message, "Ayarlar", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, $"Log klasörü açılamadı:\n{ex.Message}",
                "Klasör açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            _logger.Warning("OpenLogFolder", reason: ex.Message);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "MVP" : $"{version.Major}.{version.Minor}.{version.Build}";
        var message = $"Lens\nGörsel Ürün Arama Sistemi\nSürüm: {versionText}";
        MessageBox.Show(this, message, "Hakkında", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SelectResult(SearchResultViewModel result)
    {
        foreach (var r in _results)
        {
            r.IsSelected = ReferenceEquals(r, result);
        }

        _selectedResult = result;
        ComparisonResultImage.Source = result.Thumbnail;
        ComparisonScoreText.Text = result.ScoreText;
    }

    private void ClearComparison()
    {
        foreach (var r in _results)
        {
            r.IsSelected = false;
        }

        _selectedResult = null;
        ComparisonResultImage.Source = null;
        ComparisonScoreText.Text = string.Empty;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_productFolder is null || _indexEntries.Count == 0)
        {
            MessageBox.Show(this, "Önce bir ürün klasörü seçip index'i güncelleyin.",
                "Index yok", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_queryImagePath is null)
        {
            MessageBox.Show(this, "Önce bir sorgu görseli seçin.",
                "Görsel seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryEnsureEmbedder(out var modelError))
        {
            MessageBox.Show(this, modelError, "Model yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusy(true);

        // [Faz 4B] Search-before-refresh: cache'e korukorune guvenme. TTL
        // dolmadiysa (30 sn) hicbir I/O yapilmaz. Doldiyse ucuz bir
        // metadata-only DetectChanges calisir; degisiklik varsa gercek
        // (yalnizca degisenleri embed eden) BuildOrUpdate tetiklenir.
        var now = DateTime.UtcNow;
        if (_lastFreshnessCheckUtc is null || now - _lastFreshnessCheckUtc >= FreshnessCheckInterval)
        {
            SetIndexStatus("Klasör güncelliği kontrol ediliyor...");
            var folder = _productFolder;
            var changes = await Task.Run(() => ImageIndex.DetectChanges(folder));

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

        if (_indexEntries.Count == 0)
        {
            SetBusy(false);
            MessageBox.Show(this, "Bu klasörde indekslenmiş ürün yok.",
                "Index boş", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetIndexStatus("Aranıyor...");

        var searchStopwatch = Stopwatch.StartNew();
        try
        {
            var queryPath = _queryImagePath;
            var entries = _indexEntries;
            var embedder = _embedder!;

            var top10 = await Task.Run(() =>
            {
                var embedding = embedder.Embed(queryPath);
                return SimilaritySearch.TopK(embedding, entries, 10);
            });

            _results.Clear();
            foreach (var r in top10)
            {
                var fullPath = Path.Combine(_productFolder, r.RelativePath);
                _results.Add(new SearchResultViewModel
                {
                    FileName = r.RelativePath,
                    ScoreText = $"Benzerlik: {r.Score:P1}",
                    Thumbnail = TryLoadPreview(fullPath),
                    FullPath = fullPath,
                });
            }

            // [Faz 4D] Karsilastirma alani hicbir zaman bos kalmasin diye
            // Top-1 otomatik secilir; kullanici isterse listeden baskasina gecer.
            if (_results.Count > 0)
            {
                SelectResult(_results[0]);
            }
            else
            {
                ClearComparison();
            }

            searchStopwatch.Stop();
            SetIndexStatus($"{top10.Count} sonuç bulundu.", success: true);
            _logger.Info("Search", file: Path.GetFileName(queryPath),
                reason: $"results={top10.Count} duration_ms={searchStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            SetIndexStatus("Arama başarısız oldu.", success: false);
            _logger.Error("Search", file: _queryImagePath, reason: ex.Message);
            MessageBox.Show(this, $"Arama sırasında hata oluştu:\n{ex.Message}",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
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
