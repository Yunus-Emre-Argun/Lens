using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        ResultsItemsControl.ItemsSource = _results;
        _logger.Info("AppStart");
        LoadDefaultProductDirectory();
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
            IndexStatusText.Text = "Varsayılan ürün dizini yapılandırılmamış. Lütfen bir klasör seçin.";
            _logger.Info("ProductDirectory", reason: "yapılandırılmamış");
            UpdateDirectoryOriginUi();
            return;
        }

        if (!resolution.IsAccessible)
        {
            IndexStatusText.Text =
                $"Varsayılan ürün dizinine ulaşılamadı: {resolution.Directory}\nLütfen başka bir klasör seçin.";
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
        IndexStatusText.Text = _indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : "Varsayılan klasör yüklendi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.";

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
        _lastFreshnessCheckUtc = null;

        _indexEntries = ImageIndex.Load(_productFolder);
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        IndexStatusText.Text = _indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : "Klasör seçildi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.";

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
        IndexStatusText.Text = "Bu klasör kalıcı varsayılan olarak ayarlandı.";
    }

    private void ClearDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        ProductDirectoryResolver.ClearUserOverride(_logger);
        _logger.Info("UserOverride", reason: "cleared");
        _directoryOrigin = _productFolder is null ? DirectoryOrigin.None : DirectoryOrigin.Manual;
        UpdateDirectoryOriginUi();
        IndexStatusText.Text = "Kullanıcı varsayılanı temizlendi. Sonraki açılışta yönetici varsayılanı kullanılacak.";
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
        IndexStatusText.Text = "İndeksleniyor...";

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
                IndexStatusText.Text = $"İndeksleniyor... {p.Done}/{p.Total}");

            _logger.Info("IndexScan", reason: $"trigger={trigger} başladı");
            var (entries, stats) = await Task.Run(
                () => ImageIndex.BuildOrUpdate(folder, embedder, progress));

            if (stats.ScanError is not null)
            {
                IndexStatusText.Text = $"Klasör taranamadı: {stats.ScanError}";
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
            IndexStatusText.Text = BuildSummaryText(stats, entries.Count, wasFirstCreation);

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
            IndexStatusText.Text = "İndeksleme başarısız oldu.";
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

        try
        {
            QueryPreviewImage.Source = LoadPreview(dialog.FileName);
            _queryImagePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            _queryImagePath = null;
            QueryPreviewImage.Source = null;
            MessageBox.Show(this, $"Görsel önizlemesi yüklenemedi:\n{ex.Message}",
                "Görsel okunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _results.Clear();
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
            IndexStatusText.Text = "Klasör güncelliği kontrol ediliyor...";
            var folder = _productFolder;
            var changes = await Task.Run(() => ImageIndex.DetectChanges(folder));

            if (changes.ScanError is not null)
            {
                IndexStatusText.Text =
                    $"Klasör güncelliği kontrol edilemedi ({changes.ScanError}). Kayıtlı index ile aranıyor...";
                _logger.Warning("FreshnessCheck", file: folder, reason: changes.ScanError);
                // Ag gecici olarak erisilemez olabilir - kullaniciyi tamamen
                // durdurmuyoruz, elimizdeki son bilinen index ile arama
                // yapmaya devam ediyoruz.
            }
            else if (changes.HasChanges)
            {
                IndexStatusText.Text =
                    $"Değişiklik bulundu (yeni={changes.NewCount}, değişen={changes.ChangedCount}, "
                    + $"silinen={changes.RemovedCount}). İndeksleniyor...";
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

        IndexStatusText.Text = "Aranıyor...";

        var searchStopwatch = Stopwatch.StartNew();
        try
        {
            var queryPath = _queryImagePath;
            var entries = _indexEntries;
            var embedder = _embedder!;

            var top5 = await Task.Run(() =>
            {
                var embedding = embedder.Embed(queryPath);
                return SimilaritySearch.TopK(embedding, entries, 5);
            });

            _results.Clear();
            foreach (var r in top5)
            {
                var fullPath = Path.Combine(_productFolder, r.RelativePath);
                _results.Add(new SearchResultViewModel
                {
                    FileName = r.RelativePath,
                    ScoreText = $"Benzerlik: {r.Score:P1}",
                    Thumbnail = TryLoadPreview(fullPath),
                });
            }

            searchStopwatch.Stop();
            IndexStatusText.Text = $"{top5.Count} sonuç bulundu.";
            _logger.Info("Search", file: Path.GetFileName(queryPath),
                reason: $"results={top5.Count} duration_ms={searchStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            IndexStatusText.Text = "Arama başarısız oldu.";
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

public sealed class SearchResultViewModel
{
    public string FileName { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public BitmapImage? Thumbnail { get; set; }
}
