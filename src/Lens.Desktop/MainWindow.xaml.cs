using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Lens.Core.Ai;
using Lens.Core.Indexing;
using Lens.Core.Search;
using Microsoft.Win32;

namespace Lens.Desktop;

/// <summary>
/// Faz 3B: tek ekranli WPF MVP. AI/index katmani (Lens.Core) Faz 3A'da
/// dogrulanan haliyle degistirilmeden kullanilir; bu dosya yalnizca UI
/// orkestrasyonunu yapar (MVVM framework kasitli olarak kullanilmadi - YAGNI).
/// </summary>
public partial class MainWindow : Window
{
    private string? _productFolder;
    private string? _queryImagePath;
    private ClipEmbedder? _embedder;
    private List<ImageIndexEntry> _indexEntries = new();
    private readonly ObservableCollection<SearchResultViewModel> _results = new();

    public MainWindow()
    {
        InitializeComponent();
        ResultsItemsControl.ItemsSource = _results;
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

        _indexEntries = ImageIndex.Load(_productFolder);
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        IndexStatusText.Text = _indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : "Klasör seçildi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.";
    }

    private async void UpdateIndexButton_Click(object sender, RoutedEventArgs e)
    {
        if (_productFolder is null)
        {
            MessageBox.Show(this, "Önce bir ürün klasörü seçin.", "Klasör seçilmedi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var supportedCount = Directory.EnumerateFiles(_productFolder)
            .Count(f => ImageIndex.SupportedExtensions.Contains(Path.GetExtension(f)));
        if (supportedCount == 0)
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

        try
        {
            var folder = _productFolder;
            var embedder = _embedder!;
            var progress = new Progress<(int Done, int Total)>(p =>
                IndexStatusText.Text = $"İndeksleniyor... {p.Done}/{p.Total}");

            var (entries, stats) = await Task.Run(
                () => ImageIndex.BuildOrUpdate(folder, embedder, progress));

            _indexEntries = entries;
            ImageIndex.Save(folder, entries);

            ProductCountText.Text = $"{entries.Count} ürün";
            IndexStatusText.Text =
                $"Tamamlandı: yeni={stats.Added}, güncellenen={stats.Updated}, "
                + $"değişmeyen={stats.Unchanged}, silinen={stats.Removed}"
                + (stats.Errors.Count > 0 ? $", okunamayan={stats.Errors.Count}" : string.Empty);

            if (stats.Errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Bazı görseller okunamadı ve atlandı:\n" + string.Join("\n", stats.Errors),
                    "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            IndexStatusText.Text = "İndeksleme başarısız oldu.";
            MessageBox.Show(this, $"İndeksleme sırasında hata oluştu:\n{ex.Message}",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
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
        IndexStatusText.Text = "Aranıyor...";

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

            IndexStatusText.Text = $"{top5.Count} sonuç bulundu.";
        }
        catch (Exception ex)
        {
            IndexStatusText.Text = "Arama başarısız oldu.";
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
