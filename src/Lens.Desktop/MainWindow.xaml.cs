using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
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

    /// <summary>
    /// [İşlem ilerleme paneli - deney] Kısa işlemlerde panelin yanıp sönmesini
    /// önleyen 250ms'lik "iptal edilebilir gecikme". Her <see cref="BeginOperation"/>
    /// çağrısı bu SAME timer'ı Stop()+Start() ile sıfırlar - bu, hem "yeni bir
    /// işlem başladığında öncekinin gecikmiş callback'i yeni paneli etkileyemez"
    /// hem "işlem panel açılmadan bitmişse gecikmiş görev paneli asla açmaz"
    /// gereksinimlerini AYRI bir generation/token sayacı olmadan karşılar - tek
    /// bir zamanlayıcı örneği olduğundan bir Tick, o anda GERÇEKTEN armed olan
    /// (en son Start edilen) çağrıdan başka hiçbir yerden gelemez.
    /// </summary>
    private readonly DispatcherTimer _operationShowTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>[Model yükleme - arka plana taşıma] Aynı anda iki ClipEmbedder oluşturulmasını engeller (bkz. TryEnsureEmbedderAsync).</summary>
    private readonly SemaphoreSlim _embedderInitLock = new(1, 1);

    private static readonly CultureInfo TurkishNumberCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>[Faz 1] Son BAŞARILI taramanın istatistikleri - başarısız bir tarama bunu değiştirmez (bkz. RunIndexUpdateAsync / UpdateStatsUi, Faz 2).</summary>
    private IndexUpdateStats? _lastSuccessfulStats;

    /// <summary>[Tema turu] Aktif tema - menu check-state'i ve tema degisiminde ekranda zaten
    /// gorunen (imperatif atanmis) renklerin yeniden uygulanmasi icin tutulur. Bu alan yalnizca
    /// constructor tamamlanana kadar gecerlidir - constructor icinde SetTheme(ParseTheme(...))
    /// her zaman gercek kayitli/varsayilan temayla UZERINE YAZAR (bkz. asagida). [2026-09-08]
    /// Baslangic degeri, kayitli tema fallback'iyle (UserSettings.Theme/ParseTheme) TUTARLI
    /// olmasi icin AppTheme.Lime yapildi - pratikte gozlemlenebilir bir fark yaratmaz.</summary>
    private AppTheme _currentTheme = AppTheme.Lime;

    /// <summary>[Tema turu] En son EKRANDA GORUNEN durum metninin success parametresi (arama-ozel
    /// gecici mesajlar dahil) - tema degistiginde IndexStatusText.Foreground'u METNE DOKUNMADAN
    /// yeniden hesaplamak icin (bkz. RefreshThemeDependentForegrounds).</summary>
    private bool? _lastIndexStatusSuccess;

    /// <summary>
    /// [Yeni Arama - durum ayrımı, 2026-09-08] En son GERÇEK indeks/klasör durum mesajı
    /// (SetIndexStatus tarafından yazılır - klasör yükleme, indeksleme, freshness kontrolü,
    /// kilit/kaydetme hatası vb.). Aramaya özgü GEÇİCİ mesajlar ("Aranıyor...", "N sonuç
    /// gösteriliyor.", "Arama başarısız oldu." vb. - bkz. SetSearchStatus, SearchButton_Click)
    /// BU ALANLARA YAZILMAZ. "Yeni Arama" (NewSearchButton_Click), arama-özel geçici mesajı
    /// kaldırıp RestoreIndexStatus ile bu son GERÇEK duruma döner - böylece gerçek bir index
    /// hatası/uyarısı varsa sahte bir "hazır" mesajıyla ÖRTÜLMEZ (bkz. talimat).
    /// </summary>
    private string _lastIndexOrFolderStatusText = "—";
    private bool? _lastIndexOrFolderStatusSuccess;

    /// <summary>
    /// [Sorgu kilidi] SetBusy(true)/(false) cagrilarinin ic ice (nested) gelme
    /// ihtimaline karsi sayac - yalnizca DERINLIK SIFIRA donunce arayuz gercekten
    /// "mesgul degil" sayilir. Su an hicbir cagiran (Search/UpdateIndex/SelectFolder)
    /// baska birini nested cagirmiyor, ama bu sayac sayesinde ileride biri nested
    /// SetBusy(false) cagirsa bile DIS islemin korumasini erken KALDIRAMAZ.
    /// </summary>
    private int _busyDepth;

    private bool IsBusy => _busyDepth > 0;

    /// <summary>
    /// [Klasör adresi elle girme - yarış durumu önlemi] Kullanıcı KENDİ klasör
    /// seçimini/adresini uygulamaya başlar başlamaz (bkz. ApplyNewProductFolderAsync)
    /// true olur ve bir daha asla false'a dönmez. Bu noktadan sonra
    /// LoadDefaultProductDirectoryAsync'in gecikmiş herhangi bir devamı, kendi
    /// sonucunu (varsayılan klasör) sessizce UYGULAMADAN durur - kullanıcının
    /// daha yeni seçimini veya o sırada yazmakta olduğu taslağı EZMEZ (bkz.
    /// ShouldSkipStartupDefaultLoad).
    /// </summary>
    private bool _userInitiatedFolderChange;

    public MainWindow()
    {
        InitializeComponent();
        ClampWindowToWorkArea();
        _operationShowTimer.Tick += OperationShowTimer_Tick;
        ResultsItemsControl.ItemsSource = _results;
        _logger.Info("AppStart");

        // [Sürüm bilgisi] Tek kaynak Assembly metadata (bkz. AppVersionInfo) - Hakkında
        // ekranı (AboutMenuItem_Click) AYNI metodu çağırır, iki farklı yerde tekrar
        // okuma mantığı yok, dolayısıyla ikisi asla farklı sürüm gösteremez.
        FooterVersionText.Text = $"Sürüm: {AppVersionInfo.GetDisplayVersion()}";

        var userSettings = UserSettings.Load(_logger);
        AutoIndexCheckBox.IsChecked = userSettings.AutoIndexBeforeSearch;
        // [Sonuç sınırı] Kayıtlı deger bozuk/aralik disiysa (ör. elle duzenlenmis
        // JSON'da 0 veya 500) guvenle varsayilana (20) donulur - bu alanin kendisi
        // hicbir dogrulama yapmadigi icin kontrol burada yapiliyor (bkz. UserSettings.PreferredMaxResults).
        MaxResultsTextBox.Text = MaxResultsPreference.ValidateOrDefault(userSettings.PreferredMaxResults).ToString();
        // [Arama varsayilanlari] Acilista kutu SimilarityThreshold.DefaultPercent (80)
        // ile dolu gelir - bu deger icin kalici bir UserSettings alani YOK (bilerek,
        // bkz. talimat); "Ara" sirasinda kutu bos/yalnizca-bosluklu birakilirsa AYNI
        // sabit tekrar kullanilir (bkz. SearchButton_Click -> SimilarityThreshold.ResolveOrDefault).
        ThresholdTextBox.Text = SimilarityThreshold.DefaultPercent.ToString(CultureInfo.InvariantCulture);
        // [Yerlesim - deney] Baslangic durumu XAML varsayilanlariyla ZATEN tutarli (ikisi de
        // Visible) - burada acikca cagirmak, kodun state'e nasil baglandigini XAML'e GUVENMEDEN
        // gostermek icin savunmaci bir adim, davranis DEGISTIRMEZ.
        UpdateQueryEmptyStateVisibility();
        UpdateComparisonEmptyStateVisibility();
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

        // [Yerlesim - Eylul 2026 taslak duzeltmesi] Gorsel kutulari + ayar/buton
        // sutunlari + bosluklar + ust satirdaki varsayilan-buton-grubunun konumu
        // artik TEK bir yerlesim hesabiyla (bkz. UpdateResponsiveLayout) pencere
        // genisligine gore ayarlanir. Loaded, ilk tam layout gectikten SONRA
        // (butun sutunlarin ActualWidth'i guvenilir oldugunda) bir kez cagirir;
        // SizeChanged sonraki her pencere yeniden boyutlandirmasinda gunceller.
        Loaded += (_, _) => UpdateResponsiveLayout();
        RootGrid.SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    /// <summary>
    /// [Ekran uyumu - farklı çözünürlük/DPI analizi, 2026-09-07] XAML'deki onaylı
    /// 860×680 minimum / 1060×840 başlangıç pencere ölçüleri normal/geniş ekranlarda
    /// (ör. 1920×1080 %100/%125/%150, 1366×768 %100) hiçbir şekilde DEĞİŞMEZ - bu
    /// metot yalnızca gerçek çalışma alanı (görev çubuğu hariç, `SystemParameters.
    /// WorkArea` başlangıç anındaki mevcut ekranı DIP cinsinden yansıtır) bu
    /// sabitlerden GERÇEKTEN küçükse devreye girer. Ölçülen/hesaplanan risk: bazı
    /// yaygın dizüstü + Windows ölçeklendirme kombinasyonlarında (ör. 1366×768 %125
    /// ~1093×574 DIP, %150 ~911×472 DIP çalışma alanı) sabit `MinHeight=680` bile
    /// ekrandan BÜYÜK kalıyordu - WPF interaktif resize'da `MinHeight`/`MinWidth`
    /// altına asla izin vermediğinden kullanıcı pencereyi HİÇBİR ŞEKİLDE küçültüp
    /// ekrana sığdıramıyordu (sonuçlar/alt bilgi kalıcı olarak erişilemez kalırdı).
    /// `Math.Min`/`Math.Clamp` ile hem `MinWidth`/`MinHeight` hem de başlangıç
    /// `Width`/`Height` çalışma alanını AŞMAYACAK şekilde (yalnızca gerektiğinde)
    /// küçültülür - hiçbir zaman BÜYÜTÜLMEZ, dolayısıyla onaylanmış tasarım normal
    /// ekranlarda birebir korunur. Orta/üst bölüm kontrol boyutları, boşluklar ve
    /// `UpdateResponsiveLayout`'un görsel/sütun hesapları BURADAN ETKİLENMEZ - o
    /// metot zaten yalnızca `RootGrid.ActualWidth`'e göre çalışır ve pencere ne
    /// kadar küçültülürse küçültülsün aynı şekilde tepki verir. Çoklu monitör/DPI
    /// değişikliği (uygulama açıkken ekran değiştirme) kapsam dışı bırakıldı -
    /// `SystemParameters.WorkArea` yalnızca BAŞLANGIÇ anındaki birincil ekranı
    /// yansıtır, bu görevin istediği 6 tek-monitör senaryosu için yeterlidir.
    /// </summary>
    private void ClampWindowToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return; // Guvenilmez/olculemeyen calisma alani - dokunma.
        }

        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Clamp(Width, MinWidth, workArea.Width);
        Height = Math.Clamp(Height, MinHeight, workArea.Height);
    }

    /// <summary>
    /// [Polish] Tek pencere-genisligi orani "t" (0=Window.MinWidth, 1=hesaplanan "tam genis
    /// hedef"), gorsel genisligi (240..320, sonra height-budget ile ayrica sinirlanir - bkz.
    /// asagi), orta sutun genisligi (280..320, Ara/Yeni Arama+ARAMA AYARLARI paneli AYNI) ve
    /// iki gorsel-panel boslugu (24..50) icin ORTAK olarak kullanilir. Sonda, hesaplanan toplam
    /// genislik RootGrid.ActualWidth'i asarsa sirayla (1) bosluk (2) orta sutun (3) gorsel
    /// kucultulerek yatay tasma engellenir (bkz. talimat "kucculme sirasi").
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        if (RootGrid.ActualWidth <= 0 || RootGrid.ActualHeight <= 0)
        {
            return;
        }

        const double narrowWindowWidth = 860; // Window.MinWidth
        const double narrowImage = 240, wideImage = 320;
        const double narrowMiddleColumn = 280, wideMiddleColumn = 320;
        const double narrowGap = 24, wideGapOuter = 50;
        const double narrowFolderPath = 180, wideFolderPath = 530;
        const double narrowProductInfoMax = 160, wideProductInfoMax = 600;

        const double wideTotalContent = wideImage + wideGapOuter + wideMiddleColumn + wideGapOuter + wideImage;
        const double wideWindowWidth = wideTotalContent + 24 /*RootGrid Margin*/ + 20 /*pencere cercevesi payi*/;

        var t = Math.Clamp((RootGrid.ActualWidth - narrowWindowWidth) / (wideWindowWidth - narrowWindowWidth), 0, 1);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        var middleColumnWidth = Lerp(narrowMiddleColumn, wideMiddleColumn, t);
        var gapOuter = Lerp(narrowGap, wideGapOuter, t);
        var folderPathWidth = Lerp(narrowFolderPath, wideFolderPath, t);
        FolderPathColumn.Width = new GridLength(folderPathWidth);
        var productInfoMaxWidth = Lerp(narrowProductInfoMax, wideProductInfoMax, t);
        ProductInfoPanel.MaxWidth = productInfoMaxWidth;

        // [Yukseklik butcesi] TopAreaGrid/FooterGrid GERCEK olculmus yukseklikler; digerleri
        // (queryChromeHeight/resultsHeaderHeight/minResultsReserve) XAML yapisindan STATIK
        // tahmin - canli DPI olcumu DEGIL (bkz. son rapor).
        const double queryChromeHeight = 66; // gorsel basligi + dosya-adi + kalici ipucu satiri
        const double resultsHeaderHeight = 26; // "EN BENZER SONUÇLAR (N)" basligi
        const double minResultsReserve = 120; // sonuc alani icin asgari rezerv
        const double comparisonRowVerticalMargin = 32; // ComparisonRowGrid Margin (0,20,0,12)
        const double rootMargins = 24; // RootGrid Margin=12 (ust+alt)

        var availableForMiddleRow = RootGrid.ActualHeight
            - TopAreaGrid.ActualHeight
            - FooterGrid.ActualHeight
            - rootMargins
            - comparisonRowVerticalMargin
            - resultsHeaderHeight
            - minResultsReserve;
        var maxImageHeightFromSpace = availableForMiddleRow - queryChromeHeight;
        var maxImageWidthFromSpace = maxImageHeightFromSpace / 0.75;

        // [Polish - taşma düzeltmesi] Eskiden alt sinir narrowImage(240) idi, yukseklik butcesi
        // bunun altini istese bile gorsel kucultulemiyordu ("120 DIP rezerv garantisi" yanlisti).
        // Mutlak guvenli taban artik 200 - normal responsive taban (240) yalnizca bir HEDEF.
        const double absoluteFloorImage = 200;
        var widthDrivenImage = Math.Clamp(Lerp(narrowImage, wideImage, t), narrowImage, wideImage);
        var imageWidth = Math.Clamp(Math.Min(widthDrivenImage, maxImageWidthFromSpace), absoluteFloorImage, wideImage);
        var imageHeight = imageWidth * 0.75; // 4:3, tek formul (dar/genis ayri tablo yok)

        // [Yatay tasma guvenligi] Gercek kullanilabilir genislikle karsilastir; asarsa sirayla
        // (1) dis bosluk (2) orta sutun (3) gorsel (4:3 korunarak) kucultulur.
        var totalMiddleWidth = (imageWidth * 2) + (gapOuter * 2) + middleColumnWidth;
        if (totalMiddleWidth > RootGrid.ActualWidth)
        {
            var overflow = totalMiddleWidth - RootGrid.ActualWidth;

            const double gapFloor = 16;
            var gapReduction = Math.Min(overflow, Math.Max(0, (gapOuter - gapFloor) * 2));
            gapOuter -= gapReduction / 2;
            overflow -= gapReduction;

            if (overflow > 0)
            {
                var middleReduction = Math.Min(overflow, Math.Max(0, middleColumnWidth - narrowMiddleColumn));
                middleColumnWidth -= middleReduction;
                overflow -= middleReduction;
            }

            if (overflow > 0)
            {
                var imageReduction = Math.Min(overflow, Math.Max(0, (imageWidth - absoluteFloorImage) * 2));
                imageWidth -= imageReduction / 2;
                imageHeight = imageWidth * 0.75;
            }
        }

        QueryDropZone.Width = imageWidth;
        QueryDropZone.Height = imageHeight;
        ComparisonResultBorder.Width = imageWidth;
        ComparisonResultBorder.Height = imageHeight;
        QueryFileNameText.Width = imageWidth;
        ComparisonFileNameText.Width = imageWidth;
        QueryDropHintText.Width = imageWidth;
        QueryEmptyStatePanel.MaxWidth = Math.Max(0, imageWidth - 24);
        ComparisonEmptyStateText.MaxWidth = Math.Max(0, imageWidth - 24);
        // [Polish] Watermark font boyutu: 18(dar)-22(genis) ana, 12-13 alt satir.
        QueryEmptyTitleText.FontSize = Lerp(18, 22, t);
        QueryEmptyHintText.FontSize = Lerp(12, 13, t);

        ComparisonGapLeftColumn.Width = new GridLength(gapOuter);
        ComparisonGapRightColumn.Width = new GridLength(gapOuter);
        SettingsButtonsGrid.Width = middleColumnWidth;

        // [Ust satir hizalama] Varsayilan buton grubunun (sutun 4-5) sol baslangicini
        // karsilastirma satirindaki SAG gorselin sol kenari civarinda tutar - bkz. XAML yorumu.
        var comparisonTotalWidth = (imageWidth * 2) + (gapOuter * 2) + middleColumnWidth;
        var comparisonLeftEdgeX = Math.Max(0, (RootGrid.ActualWidth - comparisonTotalWidth) / 2);
        var rightImageStartX = comparisonLeftEdgeX + imageWidth + gapOuter + middleColumnWidth + gapOuter;

        // [Sinir-durumu duzeltmesi] FolderPathColumn.ActualWidth BURADA KULLANILMAZ - ActualWidth
        // bu satirin birkac satir YUKARISINDA ayarlanan Width'i henuz YANSITMAZ (WPF, Width
        // atamasindan sonraki bir sonraki measure/arrange turune kadar ActualWidth'i
        // GUNCELLEMEZ), yani bir onceki pencere-boyutu turunun DEGERINI okur - bu, yeniden
        // boyutlandirmada varsayilan buton grubunun bir kare GERIDEN "sicramasina" yol acardi.
        // Bunun yerine BU TURDA zaten hesaplanan folderPathWidth (ColumnDefinition'in gercek
        // MinWidth/MaxWidth sinirlariyla ayni sekilde Clamp edilmis hali) dogrudan kullanilir -
        // bu deger, Grid'in bu turda GERCEKTEN uygulayacagi genislikle BIREBIR ayni.
        var leftContentWidth = SelectFolderColumn.ActualWidth
            + Math.Clamp(folderPathWidth, FolderPathColumn.MinWidth, FolderPathColumn.MaxWidth)
            + ProductInfoColumn.ActualWidth;
        var defaultGroupNaturalWidth = DefaultGroupColumn.ActualWidth
            + ClearDefaultColumn.ActualWidth;
        // Menu her zaman en sagda kalmasi gereken bagimsiz bir kontrol - burada yalnizca
        // DefaultGroupSpacerColumn'un menuyu sikistirmayacak kadar alan BIRAKMASI icin
        // rezerve edilir (kod menuyu KONUMLANDIRMAZ, sadece komsu spacer'in asiri
        // buyumesini engeller; asil konumlandirma MenuSpacerColumn'daki gercek "*" ile olur).
        var menuNaturalWidth = MenuColumn.ActualWidth;

        var targetSpacer = Math.Max(0, rightImageStartX - leftContentWidth);
        var maxAvailableSpacer = Math.Max(0, RootGrid.ActualWidth - leftContentWidth - defaultGroupNaturalWidth - menuNaturalWidth);
        DefaultGroupSpacerColumn.Width = new GridLength(Math.Min(targetSpacer, maxAvailableSpacer));
    }

    /// <summary>[Faz 1] Checkbox tercihi degistiginde aninda kalicilastirilir (bkz. UserSettings.AutoIndexBeforeSearch).</summary>
    private void AutoIndexCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        var settings = UserSettings.Load(_logger);
        settings.AutoIndexBeforeSearch = AutoIndexCheckBox.IsChecked == true;
        settings.Save(_logger);
    }

    /// <summary>
    /// [Faz 4D] GERÇEK indeks/klasör durumu için (klasör yükleme, indeksleme, freshness
    /// kontrolü, kilit/kaydetme hatası vb.) - ana ekrandaki durum metnini, sonucun niteligine
    /// gore hafif bir renk vurgusuyla gosterir (success=yesil, warning/hata=kirmizimsi,
    /// null=notr). Salt UI vurgusu - IndexUpdateStats/log icerigini etkilemez.
    /// [2026-09-08] Ayrıca en son GERÇEK durumu (_lastIndexOrFolderStatus*) kaydeder ki
    /// "Yeni Arama" sonrası RestoreIndexStatus buraya dönebilsin - aramaya özgü GEÇİCİ
    /// mesajlar için bunun yerine SetSearchStatus kullanılmalıdır (bkz. "durumların
    /// kapsamını karıştırma" talimatı).
    /// </summary>
    private void SetIndexStatus(string text, bool? success = null)
    {
        _lastIndexOrFolderStatusText = text;
        _lastIndexOrFolderStatusSuccess = success;
        ApplyStatusText(text, success);
    }

    /// <summary>
    /// [Yeni Arama - durum ayrımı, 2026-09-08] Aramaya ÖZGÜ GEÇİCİ durum mesajları için
    /// ("Aranıyor...", "N sonuç gösteriliyor.", "Arama başarısız oldu." vb. - bkz.
    /// SearchButton_Click). SetIndexStatus'un aksine _lastIndexOrFolderStatus* BASELINE'ini
    /// GÜNCELLEMEZ - böylece "Yeni Arama" (RestoreIndexStatus), bu geçici mesajı atlayıp
    /// en son GERÇEK indeks/klasör durumuna güvenle dönebilir.
    /// </summary>
    private void SetSearchStatus(string text, bool? success = null) => ApplyStatusText(text, success);

    /// <summary>
    /// [Yeni Arama - durum ayrımı, 2026-09-08] "Yeni Arama" sırasında, önceki aramadan kalan
    /// geçici durum mesajını ("N sonuç gösteriliyor." vb.) kaldırıp en son GERÇEK indeks/
    /// klasör durumuna (SetIndexStatus tarafından yazılan) döner. Gerçek bir index hatası/
    /// kalıcı uyarı varsa AYNEN korunur - sahte bir "hazır" mesajıyla ÖRTÜLMEZ.
    /// </summary>
    private void RestoreIndexStatus() => ApplyStatusText(_lastIndexOrFolderStatusText, _lastIndexOrFolderStatusSuccess);

    private void ApplyStatusText(string text, bool? success)
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
    /// <summary>
    /// [2026-09-08] Guvenli geri donus "Lime"a cevrildi (onceden "Normal" idi) -
    /// yeni kullanicilar, kayitli ayar dosyasi olmayanlar ve bos/bilinmeyen/gecersiz
    /// bir "Theme" degeri okuyanlar icin varsayilan tema artik Lime (bkz.
    /// docs/DECISIONS.md, SUPERSEDES eski "Normal varsayilandir" karari).
    /// Kullanicinin ONCEDEN ACIKCA kaydettigi GECERLI bir tema adi (ör. "Normal",
    /// "Koyu", "AcikSepya") bu degisiklikten ETKILENMEZ - Enum.TryParse basarili
    /// oldugu surece aynen parse edilip DONDURULUR, hicbir zaman Lime'a cevrilmez.
    /// </summary>
    private static AppTheme ParseTheme(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(typeof(AppTheme), parsed))
        {
            return parsed;
        }

        return AppTheme.Lime;
    }

    /// <summary>
    /// [Polish] Arama Ayarlari paneli icin tema basina SABIT yuzey - artik ana pencere
    /// zemininden turetilmiyor (Lime'da kirli sari-yesil sonuc veriyordu). AppTheme.cs/
    /// ThemePalette'e DOKUNULMADI - bu, ondan tamamen bagimsiz, yalnizca panel icin ayri bir
    /// tablo (talimattaki hex degerler).
    /// </summary>
    private static (Color Background, Color Foreground, Color SecondaryText, Color Border) GetSettingsPanelColors(AppTheme theme) => theme switch
    {
        AppTheme.Acik => (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x1F, 0x29, 0x37), Color.FromRgb(0x6B, 0x72, 0x80), Color.FromRgb(0xCB, 0xD5, 0xE1)),
        AppTheme.Lime => (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x1F, 0x29, 0x37), Color.FromRgb(0x6B, 0x72, 0x80), Color.FromRgb(0xCB, 0xD5, 0xE1)),
        AppTheme.AcikSepya => (Color.FromRgb(0xFF, 0xF9, 0xF0), Color.FromRgb(0x2A, 0x21, 0x18), Color.FromRgb(0x6B, 0x5D, 0x4D), Color.FromRgb(0xD8, 0xC7, 0xAD)),
        AppTheme.Koyu => (Color.FromRgb(0x47, 0x55, 0x69), Colors.White, Color.FromRgb(0xD1, 0xD9, 0xE3), Color.FromRgb(0x64, 0x74, 0x8B)),
        AppTheme.KoyuSepya => (Color.FromRgb(0x80, 0x6B, 0x54), Colors.White, Color.FromRgb(0xE4, 0xD9, 0xC7), Color.FromRgb(0x9A, 0x84, 0x6A)),
        // Normal: sorgu/sonuc kartlariyla gorsel butunluk icin cok hafif kirik beyaz.
        _ => (Color.FromRgb(0xF8, 0xFA, 0xFC), Color.FromRgb(0x1F, 0x29, 0x37), Color.FromRgb(0x6B, 0x72, 0x80), Color.FromRgb(0xCB, 0xD5, 0xE1)),
    };

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
        var panelColors = GetSettingsPanelColors(theme);
        Resources["SettingsPanelBackgroundBrush"] = new SolidColorBrush(panelColors.Background);
        Resources["SettingsPanelForegroundBrush"] = new SolidColorBrush(panelColors.Foreground);
        Resources["SettingsPanelSecondaryTextBrush"] = new SolidColorBrush(panelColors.SecondaryText);
        Resources["SettingsPanelBorderBrush"] = new SolidColorBrush(panelColors.Border);

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
            if (ShouldSkipStartupDefaultLoad())
            {
                return;
            }

            SetIndexStatus("Varsayılan ürün dizinine ulaşılamadı. Lütfen bir klasör seçin.", success: false);
            _logger.Error("ProductDirectory", reason: ex.Message);
            UpdateDirectoryOriginUi();
            return;
        }

        // [Yarış durumu önlemi] İlk await'in ("ResolveDefault") sürdüğü sürede
        // kullanıcı KENDİ klasör seçimini/adresini uygulamış veya yazmaya
        // başlamışsa, gecikmiş varsayılan yükleme burada sessizce durur - ne
        // aktif klasörü/adres kutusunu ne de sonraki hiçbir UI durumunu EZMEZ.
        if (ShouldSkipStartupDefaultLoad())
        {
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

        // [Yarış durumu önlemi] İkinci await ("index yükleme") sürerken de
        // kullanıcı araya girmiş olabilir - _productFolder/FolderPathTextBox
        // yukarıda zaten değişmişti, ama kullanıcının KENDİ işlemi bu arada
        // tamamlanmışsa (ApplyNewProductFolderAsync) zaten KENDİ doğru
        // değerlerini yazmış olacaktır; burada geriye bir şey YAPILMAZ.
        if (ShouldSkipStartupDefaultLoad())
        {
            return;
        }

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
        if (IsBusy)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Tarama Klasörünü Seçin" };
        if (dialog.ShowDialog() != true)
        {
            // [Talimat] Iptal, mevcut taslagi/uygulanmis durumu BOZMAZ - hicbir
            // sey degismeden erken donulur.
            return;
        }

        SetBusy(true);
        try
        {
            await ApplyNewProductFolderAsync(dialog.FolderName, DirectoryOrigin.Manual);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// [Klasör adresi elle girme - PAYLAŞILAN geçiş yolu] Hem "Tarama Klasörünü Seç"
    /// (dialog) hem de elle adres uygulama (Enter) AYNI bu metodu kullanır -
    /// iki farklı davranış geliştirilmedi (bkz. talimat). Çağrıdan ÖNCE
    /// candidateFolder'ın var/erişilebilir olduğu ZATEN doğrulanmış olmalıdır.
    /// Gerçekten FARKLI bir klasöre geçişte eski sonuçlar/seçili sonuç/kaydırma/
    /// istatistikler/sorunlu dosya listesi YENİ klasöre TAŞINMAZ; AYNI klasörün
    /// tekrar uygulanması (normalize edilmiş karşılaştırma, büyük/küçük harf
    /// duyarsız) hiçbirini gereksiz yere temizlemez/varsayılan kaynağını
    /// değiştirmez - yalnızca index sessizce tazelenir. Çağıran taraf SetBusy
    /// (try/finally) ile sarmalamalıdır.
    /// </summary>
    private async Task ApplyNewProductFolderAsync(string candidateFolder, DirectoryOrigin origin)
    {
        // [Yarış durumu önlemi] Kullanıcı KENDİ işlemini başlattığı andan
        // itibaren gecikmiş bir varsayılan-yükleme devamı asla araya giremez
        // (bkz. ShouldSkipStartupDefaultLoad) - bu bayrak bir daha false OLMAZ.
        _userInitiatedFolderChange = true;

        var normalizedCandidate = Path.GetFullPath(candidateFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalizedCandidate.Length == 0)
        {
            normalizedCandidate = candidateFolder;
        }

        var isDifferentFolder = _productFolder is null
            || !string.Equals(_productFolder, normalizedCandidate, StringComparison.OrdinalIgnoreCase);

        _productFolder = normalizedCandidate;
        FolderPathTextBox.Text = normalizedCandidate;

        if (isDifferentFolder)
        {
            // [Talimat] Eski klasore ait sonuclar/secili sonuc/kaydirma/
            // istatistikler/sorunlu dosya listesi YENI klasore TASINMAZ.
            _results.Clear();
            UpdateResultsHeaderText();
            ClearComparison();
            ResetResultsScroll();
            _lastFreshnessCheckUtc = null;
            _lastSuccessfulStats = null;
            _lastIssues = Array.Empty<IndexFileIssue>();
            UpdateStatsUi();
            UpdateProblemFilesUi();
        }

        SetIndexStatus("Paylaşılan index yükleniyor...");
        var folder = normalizedCandidate;
        // [Faz 1 - shared index network safety] bkz. LoadDefaultProductDirectoryAsync.
        var (hadCacheFile, loadedEntries) = await Task.Run(() =>
            (File.Exists(ImageIndex.IndexPath(folder)), ImageIndex.Load(folder, _logger)));

        _indexEntries = loadedEntries;
        ProductCountText.Text = $"{_indexEntries.Count} ürün (kayıtlı index)";
        SetIndexStatus(_indexEntries.Count > 0
            ? "Kayıtlı index yüklendi. Yeni/değişen görsel varsa taramak için 'İndeksi Güncelle'ye basın."
            : hadCacheFile
                ? "Kayıtlı index okunamadı (bozuk veya uyumsuz). 'İndeksi Güncelle' ile yeniden oluşturabilirsiniz."
                : "Klasör seçildi. İndekslemek için 'İndeksi Güncelle / Klasörü Tara' butonuna basın.");

        // [Faz 4A] Manuel secim/adres varsayilan olarak GECICIDIR (session-only) -
        // burada hicbir ayar dosyasina yazilmaz. Kalici hale getirmek icin
        // kullanici "Bu Klasoru Varsayilan Yap" butonuna basmali.
        _directoryOrigin = origin;
        UpdateDirectoryOriginUi();
        _logger.Info("ProductDirectory", file: normalizedCandidate, reason: origin.ToString());
    }

    // ---- [Klasör adresi elle girme] FolderPathTextBox artık salt-görüntüleme
    // değil - kullanıcı yazabilir/yapıştırabilir. Enter: taslağı doğrula ve
    // başarılıysa uygula (aynı Enter aramayı BAŞLATMAZ - e.Handled=true).
    // Esc: düzenlemeyi iptal et, aktif klasöre dön. Odak kaybı TEK BAŞINA
    // hiçbir şeyi uygulamaz (kasıtlı olarak bir LostFocus-apply handler'ı YOK).

    private void FolderPathTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true; // Enter'in baska bir varsayilan butona (Ara) gitmesini engelle.
            _ = TryApplyFolderPathDraftAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelFolderPathEdit();
        }
    }

    private void FolderPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFolderPathHint();
    }

    /// <summary>[Klasör adresi elle girme] Kutuda aktif _productFolder'dan FARKLI, henüz Enter ile onaylanmamış bir taslak varsa true döner.</summary>
    private bool HasUnappliedFolderPathEdit() => FolderPathTextBox.Text != (_productFolder ?? string.Empty);

    /// <summary>
    /// [Yarış durumu önlemi] bkz. _userInitiatedFolderChange. Kullanıcı KENDİ
    /// bir işlemi zaten başlattıysa (bayrak true) VEYA o anda kutuda henüz
    /// uygulanmamış bir taslak varsa (bayrak henüz false olsa bile - ör.
    /// kullanıcı Enter'a basmadan önce yazmaya başlamışsa), gecikmiş
    /// LoadDefaultProductDirectoryAsync devamı durur.
    /// </summary>
    private bool ShouldSkipStartupDefaultLoad() => _userInitiatedFolderChange || HasUnappliedFolderPathEdit();

    private void UpdateFolderPathHint()
    {
        if (!HasUnappliedFolderPathEdit())
        {
            FolderPathHintText.Visibility = Visibility.Collapsed;
            return;
        }

        FolderPathHintText.Text = "Adresi uygulamak için Enter, iptal etmek için Esc.";
        FolderPathHintText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        FolderPathHintText.Visibility = Visibility.Visible;
    }

    private void ShowFolderPathValidationError(string message)
    {
        FolderPathHintText.Text = message;
        FolderPathHintText.Foreground = (Brush)FindResource("WarningBrush");
        FolderPathHintText.Visibility = Visibility.Visible;
    }

    private void CancelFolderPathEdit()
    {
        FolderPathTextBox.Text = _productFolder ?? string.Empty;
        FolderPathTextBox.CaretIndex = FolderPathTextBox.Text.Length;
        UpdateFolderPathHint();
    }

    /// <summary>
    /// [Klasör adresi elle girme] Enter'da çağrılır. Biçim doğrulaması
    /// (ProductFolderPathInput.TryNormalizeFormat, disk erişimi GEREKTİRMEZ)
    /// senkron yapılır; disk erişimi (dosya mı/klasör var mı) arka planda
    /// (Task.Run) yapılır ki arayüz bloke OLMASIN. Başarısızlıkta _productFolder/
    /// sonuçlar/index'e HİÇ DOKUNULMAZ - eski çalışan durum AYNEN korunur.
    /// </summary>
    private async Task TryApplyFolderPathDraftAsync()
    {
        if (IsBusy)
        {
            // [Sorgu kilidi benzeri] Arama/indeksleme/klasor yukleme surerken
            // adres uygulanamaz - kutu zaten IsReadOnly, ama bu bagimsiz bir
            // guvenlik agidir (bkz. talimat "ilgili olay isleyicileri de korunsun").
            return;
        }

        if (!ProductFolderPathInput.TryNormalizeFormat(FolderPathTextBox.Text, out var candidate, out var formatError))
        {
            ShowFolderPathValidationError(formatError);
            return;
        }

        SetBusy(true);
        try
        {
            bool isFile;
            bool dirExists;
            try
            {
                (isFile, dirExists) = await Task.Run(() => (File.Exists(candidate), Directory.Exists(candidate)));
            }
            catch (Exception ex)
            {
                ShowFolderPathValidationError($"Klasöre erişilemiyor: {ex.Message}");
                _logger.Warning("ManualFolderPath", file: candidate, reason: ex.Message);
                return;
            }

            if (isFile)
            {
                ShowFolderPathValidationError("Bu bir dosya adresi - lütfen bir klasör adresi girin.");
                return;
            }

            if (!dirExists)
            {
                ShowFolderPathValidationError("Bu klasöre erişilemiyor veya klasör bulunamadı.");
                return;
            }

            await ApplyNewProductFolderAsync(candidate, DirectoryOrigin.Manual);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>[Klasör adresi elle girme] Bkz. talimat: "kullanıcı ekranı taslak/aktif klasör karışmasın" - Ara/İndeksi Güncelle/Varsayılan Yap'tan önce çağrılır.</summary>
    private bool WarnIfUnappliedFolderPathEdit()
    {
        if (!HasUnappliedFolderPathEdit())
        {
            return false;
        }

        ShowFolderPathValidationError(
            "Klasör adresinde uygulanmamış bir değişiklik var. Uygulamak için Enter'a, vazgeçmek için Esc'e basın.");
        FolderPathTextBox.Focus();
        return true;
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (WarnIfUnappliedFolderPathEdit())
        {
            return;
        }

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
        if (WarnIfUnappliedFolderPathEdit())
        {
            return;
        }

        if (_productFolder is null)
        {
            AlertWindow.Show(this, "Önce bir ürün klasörü seçin.", "Klasör seçilmedi", AlertKind.Warning);
            return;
        }

        SetBusy(true);
        // [İşlem ilerleme paneli - deney] Tüm gövde tek bir finally ile
        // korunuyor - önceki dağınık `SetBusy(false); return;` noktaları
        // (aynı mesaj/davranış korunarak) tek çıkışta birleştirildi ki
        // panel HER çıkış yolunda (başarı/hata/erken-return) kapansın.
        BeginOperation("Klasör kontrol ediliyor…");
        try
        {
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
                SetIndexStatus("Ürün klasörüne şu anda ulaşılamıyor.", success: false);
                _logger.Warning("IndexPreflight", file: _productFolder, reason: ex.Message);
                AlertWindow.Show(this, "Ürün klasörüne şu anda ulaşılamıyor.", "Klasöre ulaşılamıyor", AlertKind.Warning);
                return;
            }

            if (!hasSupportedImage)
            {
                AlertWindow.Show(this, "Bu klasörde desteklenen görsel (jpg/jpeg/png) bulunamadı.",
                    "Görsel bulunamadı", AlertKind.Warning);
                return;
            }

            SetOperationStage("Arama motoru hazırlanıyor…");
            var (embedderReady, modelError) = await TryEnsureEmbedderAsync();
            if (!embedderReady)
            {
                AlertWindow.Show(this, modelError, "Model yüklenemedi", AlertKind.Error);
                return;
            }

            SetIndexStatus("İndeksleniyor...");

            // Manuel "İndeksi Güncelle" her zaman FORCE SCAN yapar (freshness
            // kontrolünü atlar) VE checkbox tercihinden BAĞIMSIZ olarak çalışır.
            // Bu, arama öncesi otomatik freshness-check'in ("Ara") de aynı işlevi
            // görecek olmasından bağımsızdır.
            await RunIndexUpdateAsync(trigger: "Manual");
        }
        finally
        {
            EndOperation();
            SetBusy(false);
        }
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

            // [İşlem ilerleme paneli - deney] Hem manuel "İndeksi Güncelle" hem
            // arama öncesi otomatik indeksleme AYNI bu metodu çağırır - aşama
            // metni tek noktadan yazılır, iki tetikleyici için ayrı kod YOK.
            SetOperationStage("Desenler indeksleniyor…", indeterminate: true);

            // [5000 dosyada mesaj kuyruğu taşması - throttle] Progress<T>.Report
            // her dosyada tetiklenir (~5000 kez); UI mesaj kuyruğunu gereksiz
            // doldurmamak için görsel güncelleme yüzde değiştiğinde VEYA ~100ms
            // geçtiğinde yapılır - SON değer (Done==Total, %100) throttle'dan
            // bağımsız HER ZAMAN geçer. Aynı throttle hem eski durum metnini
            // (SetIndexStatus) hem yeni paneli besler - iki ayrı mekanizma yok.
            var lastReportedPercent = -1;
            var lastReportTimeUtc = DateTime.MinValue;
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                var percent = p.Total > 0 ? (int)(100.0 * p.Done / p.Total) : 100;
                var isFinal = p.Done >= p.Total;
                var now = DateTime.UtcNow;
                if (!isFinal && percent == lastReportedPercent && now - lastReportTimeUtc < TimeSpan.FromMilliseconds(100))
                {
                    return;
                }

                lastReportedPercent = percent;
                lastReportTimeUtc = now;

                SetIndexStatus($"İndeksleniyor... {p.Done}/{p.Total}");
                var doneText = p.Done.ToString("N0", TurkishNumberCulture);
                var totalText = p.Total.ToString("N0", TurkishNumberCulture);
                SetOperationStage("Desenler indeksleniyor…", indeterminate: false,
                    progressValue: percent, progressText: $"{doneText} / {totalText} — %{percent}");
            });

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
            SetOperationStage("İndeks güncelliği kontrol ediliyor…");
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
            // [Kullanıcı talimatı] Bu oturumda seçili klasör için henüz başarılı bir
            // tarama yoksa alan tamamen GİZLENİR (Collapsed) - eski "Henüz başarılı
            // bir tarama yapılmadı." metni KALDIRILDI. Collapsed, Grid.Row="2"'nin
            // Auto yüksekliğinde gerçekten sıfır yer kaplar, boşluk BIRAKMAZ.
            DetailedStatsText.Text = string.Empty;
            DetailedStatsText.Visibility = Visibility.Collapsed;
            return;
        }

        var s = _lastSuccessfulStats;
        DetailedStatsText.Visibility = Visibility.Visible;
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
    /// [Durum ayrımı, 2026-09-08] Önceki aramadan kalan geçici durum mesajını
    /// ("N sonuç gösteriliyor.", "Sonuç bulunamadı.", "Arama başarısız oldu." vb.)
    /// de RestoreIndexStatus ile temizler - en son GERÇEK indeks/klasör durumuna
    /// (varsa gerçek bir hata/uyarı DAHİL) döner, sahte bir "hazır" mesajıyla
    /// ÖRTMEZ. Tarama istatistikleri (_lastSuccessfulStats/DetailedStatsText) ve
    /// sorunlu dosya listesi (_lastIssues/ProblemFilesButton) BİLEREK
    /// DOKUNULMADAN bırakılır - bunlar sorguya değil, seçili klasörün son indeks
    /// taramasına bağlıdır (bkz. talimat).
    /// </summary>
    private void NewSearchButton_Click(object sender, RoutedEventArgs e)
    {
        // [Sorgu kilidi] Buton zaten SetBusy ile gorsel olarak devre disi birakiliyor,
        // ama bu kontrol BAGIMSIZ bir guvenlik agi - ornegin klavye/otomasyon kaynakli
        // bir Click, IsEnabled=false'a ragmen event handler'a ulasirsa (WPF'te normalde
        // olmaz ama garanti degildir) arama surerken sorgu yine de degismez.
        if (IsBusy)
        {
            return;
        }

        _queryImagePath = null;
        QueryPreviewImage.Source = null;
        QueryFileNameText.Text = string.Empty;
        UpdateQueryEmptyStateVisibility();
        _results.Clear();
        UpdateResultsHeaderText();
        ClearComparison();
        ResetResultsScroll();
        RestoreIndexStatus();
    }

    /// <summary>
    /// [Yerlesim - deney] Eskiden ayri "Sorgu Görseli Seç" butonunun Click olayiydi; buton
    /// orta sutundan kaldirildigi icin (bkz. talimat) artik bos sorgu cercevesine tek tik
    /// (QueryDropZone_MouseLeftButtonDown) ve Enter/Space (QueryDropZone_KeyDown) ile
    /// PAYLASILAN tek giris noktasi - dosya secme mantigi kopyalanmadan TEK yerde kalir.
    /// </summary>
    private void OpenQuerySelectDialog()
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

        UpdateQueryEmptyStateVisibility();
        _results.Clear();
        UpdateResultsHeaderText();
        ClearComparison();
        ResetResultsScroll();
    }

    /// <summary>
    /// [Yerlesim - deney, bos-durum watermark] QueryEmptyStatePanel ("Sorgu görselini seçin" /
    /// "Tıklayın veya buraya sürükleyin"), _queryImagePath null oldugu SÜRECE gorunur - sorgu
    /// gorseli yuklendiginde TAMAMEN gizlenir, Yeni Arama ile temizlendiginde tekrar gorunur
    /// (bkz. talimat). Cagrilma noktalari: LoadQueryImage (basari/hata) ve NewSearchButton_Click.
    /// </summary>
    private void UpdateQueryEmptyStateVisibility()
    {
        QueryEmptyStatePanel.Visibility = _queryImagePath is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QueryDropZone_DragEnter(object sender, DragEventArgs e)
    {
        // [Sorgu kilidi] Arama/indeks hazirligi surerken yeni bir sorgu gorseli
        // KABUL EDILMEZ - gecerli bir dosya suruklense bile isValid zorla false
        // yapilir ki "kabul edilebilir" vurgusu (accent border/onizleme) HICBIR
        // ZAMAN yanlislikla gosterilmesin.
        var hasValidFile = TryGetDroppedImagePath(e.Data, out var path, out _);
        var isValid = !IsBusy && hasValidFile;
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
        // [Sorgu kilidi] bkz. QueryDropZone_DragEnter - ayni kural DragOver icin de gecerli.
        var isValid = !IsBusy && TryGetDroppedImagePath(e.Data, out _, out _);
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

        // [Sorgu kilidi] DragEnter/DragOver zaten "kabul edilebilir" vurgusunu hic
        // GOSTERMEDI (isValid=false), ama WPF bir Drop olayini yine de teslim
        // edebilir - bu yuzden burada da BAGIMSIZ olarak kontrol edilir. Sessizce
        // yok sayilir (kullaniciya zaten hicbir kabul sinyali verilmemisti).
        if (IsBusy)
        {
            return;
        }

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

    /// <summary>
    /// [Yerlesim - deney] "En guvenli davranis" (talimat): gorsel YOKKEN tek tik dosya secme
    /// penceresini acar (eski SelectQueryButton'un YERINE gecti); gorsel VARKEN tek tik SADECE
    /// odaklanir (yeni bir gorsel YUKLEMEZ) ki hemen ardindan gelebilecek ikinci tikin
    /// ClickCount=2 ile tetikledigi buyuk onizleme davranisi HICBIR SEKILDE bozulmasin. Cift tik
    /// (ClickCount=2) her zaman - gorsel yuklu oldugu surece - onizlemeyi acar.
    /// </summary>
    private void QueryDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            TryOpenImagePreview(_queryImagePath);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        if (_queryImagePath is null)
        {
            OpenQuerySelectDialog();
        }
        else
        {
            QueryDropZone.Focus();
        }
    }

    /// <summary>
    /// [Yerlesim - deney] Klavyeyle QueryDropZone'a odaklanip Enter/Space ile dosya secme
    /// penceresini acar (talimat) - tek-tik ile AYNI kural: yalnizca gorsel YOKKEN (gorsel
    /// varken klavyeden yeni bir gorsel YUKLENMEZ, tek-tik davranisiyla TUTARLI).
    /// </summary>
    private void QueryDropZone_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsBusy || _queryImagePath is not null)
        {
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            e.Handled = true;
            OpenQuerySelectDialog();
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
    /// [Ayarlar sadeleştirme] Sade, kullanıcı odaklı bir durum özeti - ayrı bir
    /// SettingsWindow'da gösterilir (bkz. o dosya). Ürün klasörünün TAM YOLU
    /// burada TEKRAR gösterilmez (zaten ana ekrandaki adres kutusunda var) -
    /// yalnızca kısa bir durum metni ("Geçici seçim/Kullanıcı varsayılanı/
    /// Yönetici varsayılanı/Klasör seçilmedi"). Teknik dosya yolları (yönetici
    /// config/kullanıcı ayarları/log/önbellek/model) normal kullanıcıdan
    /// varsayılan olarak KAPALI bir "Teknik ayrıntılar" bölümünde - hiçbir
    /// silme/temizleme/klasör açma KOMUTU yok, yalnızca görüntüleme. Klasör
    /// değiştirme/varsayılan yapma işlemleri hâlâ ana ekrandaki mevcut
    /// butonlarla yapılıyor (burada tekrarlanmıyor). Açmak/kapatmak hiçbir
    /// tercihi değiştirmez/kaydetmez.
    /// </summary>
    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var folderStatus = _productFolder is null
            ? "Klasör seçilmedi."
            : _directoryOrigin switch
            {
                DirectoryOrigin.AdminDefault => "Yönetici varsayılanı kullanılıyor.",
                DirectoryOrigin.UserOverride => "Kullanıcı varsayılanı kullanılıyor.",
                DirectoryOrigin.Manual => "Geçici seçim kullanılıyor (kalıcı değil - \"Bu Klasörü Varsayılan Yap\" ile kalıcı hale getirilebilir).",
                _ => "Klasör seçilmedi.",
            };

        var modelPath = ResolveModelPath() ?? "(bulunamadı)";
        var window = new SettingsWindow(
            folderStatus,
            AppPaths.AdminConfigFilePath,
            AppPaths.UserSettingsFilePath,
            AppPaths.LogsDirectory,
            AppPaths.CacheRootDirectory,
            modelPath)
        { Owner = this };
        window.ShowDialog();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // [Sürüm bilgisi] Eski GetName().Version (yalnızca sayısal AssemblyVersion)
        // KALDIRILDI - artık alt bilgi satırıyla AYNI ortak kaynağı (AppVersionInfo,
        // Assembly.InformationalVersion) kullanır, iki ekran asla farklı sürüm göstermez.
        var message = $"Lens\nGörsel Ürün Arama Sistemi\nSürüm: {AppVersionInfo.GetDisplayVersion()}";
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
        UpdateComparisonEmptyStateVisibility();
        // [Faz 4D polish] Yalnizca goruntulenen deger tam %100 oldugunda
        // basari/yesil vurgusu - diger skorlar notr kalir. SuccessBrush/NeutralTextBrush
        // artik tema-bagimli (bkz. SetTheme) - ayrica bir "OnDark..." varyanti gerekmez.
        ComparisonScoreText.Foreground = result.IsPerfectMatch
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("NeutralTextBrush");
    }

    /// <summary>
    /// [Sonuç sayısı başlıkta] Başlıktaki "(N)" - _results.Count'un DOĞRUDAN
    /// yansımasıdır: eşik VE kullanıcının "en fazla sonuç" limiti zaten
    /// uygulanmış, ekranda GERÇEKTEN listelenen kart sayısı (bkz.
    /// SearchButton_Click - IndexStatusText'teki "N sonuç gösteriliyor." ile
    /// AYNI kaynak). Yalnızca _results degistigi (Clear/Add) noktalarda
    /// cagrilir - gecersiz girdide (erken return) hic cagrilmadigindan
    /// baslik AYNEN kalir.
    /// </summary>
    private void UpdateResultsHeaderText()
    {
        ResultsHeaderText.Text = $"EN BENZER SONUÇLAR ({_results.Count})";
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
        UpdateComparisonEmptyStateVisibility();
    }

    /// <summary>
    /// [Yerlesim - deney, bos-durum placeholder] ComparisonEmptyStateText ("Henüz sonuç
    /// seçilmedi"), _selectedResult null oldugu SÜRECE gorunur - bir sonuc secildiginde
    /// TAMAMEN gizlenir (bkz. talimat). Cagrilma noktalari: SelectResult ve ClearComparison.
    /// </summary>
    private void UpdateComparisonEmptyStateVisibility()
    {
        ComparisonEmptyStateText.Visibility = _selectedResult is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// [Faz 1] Siralama: 1) urun klasoru, 2) sorgu gorseli, 3) threshold
    /// validasyonu (pahali islemlerden ONCE), 4) model hazirligi, 5) auto-index
    /// tercihine gore index hazirlama/kontrol, 6) kullanilabilir index
    /// kontrolu, 7) threshold filtreli en fazla <see cref="SimilaritySearch.MaxResults"/>
    /// (200) sonuclu arama.
    /// </summary>
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (WarnIfUnappliedFolderPathEdit())
        {
            return;
        }

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

        // [Arama varsayilanlari] Bos/yalnizca-bosluklu girdi SimilarityThreshold.
        // DefaultPercent'e (80) cozulur; TryParse'in KATI sozlesmesi (metin/negatif/
        // 100-ustu/NaN/Infinity reddi) DEGISMEDEN korunur (bkz. ResolveOrDefault).
        var thresholdInputWasEmpty = string.IsNullOrWhiteSpace(ThresholdTextBox.Text);
        if (!SimilarityThreshold.ResolveOrDefault(ThresholdTextBox.Text, out var thresholdPercent))
        {
            ShowThresholdValidationError();
            return;
        }

        HideThresholdValidationError();
        if (thresholdInputWasEmpty)
        {
            // [Talimat] Kullanici hangi degerle arandigini gorsun - yalnizca
            // BOS birakildiginda kutu geriye yazilir, kullanicinin GEREKLI bir
            // degeri (ör. "65") sessizce yeniden bicimlendirilmez/dokunulmaz.
            ThresholdTextBox.Text = thresholdPercent.ToString(CultureInfo.InvariantCulture);
        }

        var maxResultsInputWasEmpty = string.IsNullOrWhiteSpace(MaxResultsTextBox.Text);
        if (!MaxResultsPreference.ResolveOrDefault(MaxResultsTextBox.Text, out var maxResults))
        {
            // [Sonuç sınırı] Threshold ile AYNI desen: gecersiz/bos girdide erken
            // return - buradan sonraki "eski sonuclari temizle" bloguna hic
            // ULASILMAZ, dolayisiyla mevcut ekran/kaydirma AYNEN korunur. Iki
            // FARKLI mesaj: gecerli bir tam sayi ama 200'u asiyorsa ayri, diger
            // tum gecersiz durumlar (bos/metin/ondalik/negatif/0) icin genel mesaj.
            // NOT: "0" burada GECERSIZDIR (ResolveOrDefault yalnizca GERCEKTEN
            // bos/yalnizca-bosluklu girdiyi varsayilana cevirir).
            ShowMaxResultsValidationError(MaxResultsPreference.IsAboveMaxAllowed(MaxResultsTextBox.Text)
                ? $"En fazla {MaxResultsPreference.MaxAllowed} sonuç listeleyebilirsiniz."
                : $"Lütfen {MaxResultsPreference.MinAllowed}-{MaxResultsPreference.MaxAllowed} arasında bir tam sayı girin.");
            return;
        }

        HideMaxResultsValidationError();
        if (maxResultsInputWasEmpty)
        {
            MaxResultsTextBox.Text = maxResults.ToString(CultureInfo.InvariantCulture);
        }

        // [Sonuç sınırı] Yalnizca GECERLI bir deger buraya kadar gelebildigi icin
        // kalici hale getirmek guvenli - Load->degistir->Save akisi diger alanlari
        // (tema/otomatik indeksleme/klasor override'i) KORUR.
        var maxResultsSettings = UserSettings.Load(_logger);
        if (maxResultsSettings.PreferredMaxResults != maxResults)
        {
            maxResultsSettings.PreferredMaxResults = maxResults;
            maxResultsSettings.Save(_logger);
        }

        // [Stale-results fix] Buraya kadar gelindiyse girdi gecerli - "gecerli bir arama
        // baslatildi" sayilir. Index hazirligi/model yukleme gibi uzun suren islemler
        // BASLAMADAN once eski sonuclar/karsilastirma/sonuc sayisi temizlenir ki bu
        // islemler basarisiz olur veya beklenenden uzun surerse eski sonuclar YANLISLIKLA
        // yeni sorgunun sonucuymus gibi ekranda kalmasin. Sorgu gorseli, threshold girdisi,
        // tema ve kullanici ayarlari (AutoIndexBeforeSearch vb.) buradan ETKILENMEZ.
        _results.Clear();
        UpdateResultsHeaderText();
        ClearComparison();
        ResetResultsScroll();
        SetSearchStatus("Aranıyor...");

        SetBusy(true);
        BeginOperation("Arama motoru hazırlanıyor…");
        try
        {
            var (embedderReady, modelError) = await TryEnsureEmbedderAsync();
            if (!embedderReady)
            {
                SetSearchStatus("Model yüklenemedi, arama yapılamadı.", success: false);
                AlertWindow.Show(this, modelError, "Model yüklenemedi", AlertKind.Error);
                return;
            }

            var ready = await EnsureIndexReadyForSearchAsync();
            if (!ready)
            {
                // Kullanicidan aksiyon isteyen uygun mesaj EnsureIndexReadyForSearchAsync
                // icinde zaten gosterildi - burada sessizce durulur. Eski sonuclar/
                // karsilastirma yukarida zaten temizlendigi icin ekranda yaniltici bir
                // "onceki arama" gorunumu KALMAZ.
                return;
            }

            SetOperationStage("Benzer desenler aranıyor…");
            SetSearchStatus("Aranıyor...");
            var searchStopwatch = Stopwatch.StartNew();

            var queryPath = _queryImagePath;
            var entries = _indexEntries;
            var embedder = _embedder!;
            var productFolder = _productFolder;
            // [Sorgu kilidi] maxResults (threshold/queryPath gibi) burada LOCAL bir
            // degiskene sabitlenir - MaxResultsTextBox zaten SetBusy ile devre disi
            // birakildigindan degismesi beklenmez, ama arama KENDI baslangic
            // degeriyle tamamlansin diye alan ayrica arama sirasinda tekrar OKUNMAZ.
            var maxResultsForSearch = maxResults;

            // [İşlem ilerleme paneli - deney] Embed+arama, thumbnail hazırlamadan
            // AYRI bir arka plan görevine bölündü ki aradaki UI-thread dönüşünde
            // panelin aşama metni "Sonuçlar hazırlanıyor…"a geçebilsin - sıralı/
            // arka plan decode davranışının KENDİSİ değişmedi, yalnızca ikiye bölündü.
            var matches = await Task.Run(() =>
            {
                var emb = embedder.Embed(queryPath);
                return SimilaritySearch.SearchWithThreshold(emb, entries, thresholdPercent, maxResultsForSearch);
            });

            // [999-limit perf] Thumbnail decode'u (TryLoadPreview) bu arka plan
            // gorevinde kalir - eskiden UI thread'de, arama sonucu donduk-ten SONRA,
            // sirayla calisiyordu. Az sonucta gozle gorulur bir donma yaratmiyordu, ama
            // en fazla 999 sonuçta (bkz. SimilaritySearch.MaxResults) UI thread'de art
            // arda 999 JPEG decode'u fark edilir bir kilitlenmeye yol acabilirdi.
            // BitmapImage.Freeze() (bkz. LoadPreview) sayesinde arka planda olusturulan
            // gorsel donduruldukten sonra thread-safe sekilde UI'ya tasinabiliyor.
            // BILEREK sirali (paralel degil) birakildi - sabit bir donma riskini ortadan
            // kaldirmak yeterli, sinirsiz paralel decode/bellek/CPU baskisi eklenmedi.
            SetOperationStage("Sonuçlar hazırlanıyor…",
                indeterminate: matches.Count == 0,
                progressText: matches.Count > 0 ? $"0 / {matches.Count}" : null);

            // Thumbnail ilerlemesi de indeksleme ile AYNI throttle kalıbını
            // kullanır (yüzde değişti VEYA ~100ms geçti VEYA son değer) - 999
            // öğede UI mesaj kuyruğu gereksiz doldurulmaz, son değer asla atlanmaz.
            var lastReportedPercent = -1;
            var lastReportTimeUtc = DateTime.MinValue;
            IProgress<(int Done, int Total)> thumbnailProgress = new Progress<(int Done, int Total)>(p =>
            {
                var percent = p.Total > 0 ? (int)(100.0 * p.Done / p.Total) : 100;
                var isFinal = p.Done >= p.Total;
                var now = DateTime.UtcNow;
                if (!isFinal && percent == lastReportedPercent && now - lastReportTimeUtc < TimeSpan.FromMilliseconds(100))
                {
                    return;
                }

                lastReportedPercent = percent;
                lastReportTimeUtc = now;
                SetOperationStage("Sonuçlar hazırlanıyor…", indeterminate: false,
                    progressValue: percent, progressText: $"{p.Done} / {p.Total}");
            });

            var viewModels = await Task.Run(() =>
            {
                var list = new List<SearchResultViewModel>(matches.Count);
                var done = 0;
                foreach (var r in matches)
                {
                    var fullPath = Path.Combine(productFolder, r.RelativePath);
                    var scoreText = $"Benzerlik: {r.Score:P1}";
                    list.Add(new SearchResultViewModel
                    {
                        FileName = r.RelativePath,
                        ScoreText = scoreText,
                        Thumbnail = TryLoadPreview(fullPath),
                        FullPath = fullPath,
                        IsPerfectMatch = scoreText.EndsWith("100.0%", StringComparison.Ordinal),
                    });

                    done++;
                    thumbnailProgress.Report((done, matches.Count));
                }

                return list;
            });

            _results.Clear();
            foreach (var vm in viewModels)
            {
                _results.Add(vm);
            }

            // [Sonuç sayısı başlıkta] Arama TAMAMLANDIĞINDA (bu satıra kadar hata
            // olmadan gelindiyse) başlıktaki "(N)" güncellenir - eşik VE kullanıcının
            // "en fazla sonuç" limiti zaten uygulanmış, ekranda gerçekten listelenen
            // sayıyı yansıtır (viewModels.Count == _results.Count).
            UpdateResultsHeaderText();

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
                // [200-limit] Metin BILEREK "gosteriliyor" diyor, "bulundu" degil - eşiği
                // karşılayan toplam eşleşme SimilaritySearch.MaxResults'ı (200) aşarsa bu
                // sayı yalnızca EKRANDA GORUNEN (kesilmis) listeyi yansıtır, toplam
                // eşleşme sayısını değil (toplam sayı ayrıca izlenmiyor/gösterilmiyor).
                SetSearchStatus($"{_results.Count} sonuç gösteriliyor.", success: true);
            }
            else
            {
                // [Faz 1] No-result HATA DEGILDIR: onceki results/selection
                // temizlenir, query gorseli ve threshold girdisi KORUNUR,
                // modal gosterilmez - kullanici threshold'u degistirip
                // tekrar arayabilir.
                ClearComparison();
                SetSearchStatus("Seçilen minimum benzerlik değerini karşılayan sonuç bulunamadı.");
            }

            _logger.Info("Search", file: Path.GetFileName(queryPath),
                reason: $"results={_results.Count} threshold={thresholdPercent} duration_ms={searchStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            SetSearchStatus("Arama başarısız oldu.", success: false);
            _logger.Error("Search", file: _queryImagePath, reason: ex.Message);
            AlertWindow.Show(this, $"Arama sırasında hata oluştu:\n{ex.Message}", "Hata", AlertKind.Error);
        }
        finally
        {
            EndOperation();
            SetBusy(false);
        }
    }

    /// <summary>
    /// [Sayısal giriş - 2026-09-07] ThresholdTextBox/MaxResultsTextBox'a KLAVYEDEN
    /// yazılan her karakter, işlenmeden ÖNCE burada sorulur. Karar mantığı (harf/
    /// eksi işareti/fazla ayırıcı reddi) tamamen <see cref="NumericInputFilter"/>'da -
    /// WPF'siz, Lens.AiProof ile test edilebilir (bkz. hardeningtest Grup M). Hangi
    /// alanın ondalık kabul ettiği (yalnızca ThresholdTextBox) `sender`'dan belirlenir -
    /// aynı işleyici iki alan için de paylaşılır (kod tekrarı yok).
    /// </summary>
    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        var allowDecimal = ReferenceEquals(textBox, ThresholdTextBox);
        e.Handled = !NumericInputFilter.IsValidPartialInput(
            textBox.Text, textBox.SelectionStart, textBox.SelectionLength, e.Text, allowDecimal);
    }

    /// <summary>
    /// [Sayısal giriş - 2026-09-07] Yapıştırma (Ctrl+V, sağ-tık yapıştır, Düzen menüsü -
    /// hepsi AYNI WPF komutuna bağlı, tek bir yerden yakalanır) PreviewTextInput'un
    /// KAPSAMADIĞI bir yol - talimat gereği ayrıca ele alınır. Yalnızca metin verisi
    /// kabul edilir (resim/dosya yapıştırma zaten anlamsız, iptal edilir); geçerli metin
    /// bile olsa NumericInputFilter'ı geçemezse tüm yapıştırma işlemi İPTAL edilir
    /// (kısmi/bozuk bir yapıştırma bırakılmaz).
    /// </summary>
    private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        var textBox = (TextBox)sender;
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pasted = (string)e.DataObject.GetData(DataFormats.Text);
        var allowDecimal = ReferenceEquals(textBox, ThresholdTextBox);
        if (!NumericInputFilter.IsValidPartialInput(textBox.Text, textBox.SelectionStart, textBox.SelectionLength, pasted, allowDecimal))
        {
            e.CancelCommand();
        }
    }

    /// <summary>
    /// [Sayısal giriş - 2026-09-07] Son güvenlik ağı: IME kompozisyonu veya sürükle-
    /// bırak metin gibi PreviewTextInput/DataObject.Pasting'i atlayan beklenmedik bir
    /// yoldan alana geçersiz karakter girerse, burada SESSİZCE temizlenir (harf/eksi
    /// işareti/fazla ayırıcı kaldırılır, imleç konumu korunur). Zaten geçerliyse hiçbir
    /// şey yapmaz - metni yeniden ATAYIP gereksiz ikinci bir TextChanged tetiklemez.
    /// </summary>
    private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = (TextBox)sender;
        var allowDecimal = ReferenceEquals(textBox, ThresholdTextBox);
        if (NumericInputFilter.IsValidPartialText(textBox.Text, allowDecimal))
        {
            return;
        }

        var caret = textBox.CaretIndex;
        var cleaned = NumericInputFilter.StripInvalidCharacters(textBox.Text, allowDecimal);
        textBox.Text = cleaned;
        textBox.CaretIndex = Math.Min(caret, cleaned.Length);
    }

    /// <summary>[NumberBox - Polish] Yukarı/Aşağı ok tuşları da spinner düğmeleriyle AYNI adımı uygular.</summary>
    private void NumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down)
        {
            return;
        }

        e.Handled = true;
        var direction = e.Key == Key.Up ? 1 : -1;
        if (ReferenceEquals(sender, ThresholdTextBox))
        {
            StepThreshold(direction);
        }
        else
        {
            StepMaxResults(direction);
        }
    }

    private void ThresholdUpButton_Click(object sender, RoutedEventArgs e) => StepThreshold(1);
    private void ThresholdDownButton_Click(object sender, RoutedEventArgs e) => StepThreshold(-1);
    private void MaxResultsUpButton_Click(object sender, RoutedEventArgs e) => StepMaxResults(1);
    private void MaxResultsDownButton_Click(object sender, RoutedEventArgs e) => StepMaxResults(-1);

    /// <summary>
    /// [NumberBox - Polish] ThresholdTextBox'ı ±1 adımlar. Mevcut metin <see cref="SimilarityThreshold"/>
    /// sözleşmesiyle geçersizse (boş → 80 varsayılan, aralık-dışı ör. "999" → güvenli sınıra
    /// çekilir) önce güvenli bir başlangıç değerine oturtulur, sonra adım uygulanır - hiçbir
    /// zaman exception/taşma olmaz. Ondalık ayırıcı (varsa) ve basamak sayısı KORUNUR (80,5 → 81,5).
    /// </summary>
    private void StepThreshold(int direction)
    {
        var current = ResolveSteppableValue(ThresholdTextBox.Text, SimilarityThreshold.MinPercent, SimilarityThreshold.MaxPercent, SimilarityThreshold.DefaultPercent);
        var next = Math.Clamp(current + direction, SimilarityThreshold.MinPercent, SimilarityThreshold.MaxPercent);
        ThresholdTextBox.Text = FormatSteppedNumber(next, ThresholdTextBox.Text);
        ThresholdTextBox.CaretIndex = ThresholdTextBox.Text.Length;
    }

    /// <summary>[NumberBox - Polish] StepThreshold ile AYNI desen, <see cref="MaxResultsPreference"/> sözleşmesiyle (tam sayı, 1-200, varsayılan 20).</summary>
    private void StepMaxResults(int direction)
    {
        var current = ResolveSteppableValue(MaxResultsTextBox.Text, MaxResultsPreference.MinAllowed, MaxResultsPreference.MaxAllowed, MaxResultsPreference.Default);
        var next = Math.Clamp(current + direction, MaxResultsPreference.MinAllowed, MaxResultsPreference.MaxAllowed);
        MaxResultsTextBox.Text = next.ToString("0", CultureInfo.InvariantCulture);
        MaxResultsTextBox.CaretIndex = MaxResultsTextBox.Text.Length;
    }

    /// <summary>
    /// [NumberBox - Polish] Bir spinner adımı için "mevcut deger" - bos ise varsayilan, gecerli
    /// aralikta ise oldugu gibi, aralik-disi ama SAYISAL ise (ör. "999", NumericInputFilter'in
    /// 3-rakam sinirinin izin verdigi ama SimilarityThreshold/MaxResultsPreference'in reddettigi
    /// bir deger) guvenli sinira Clamp edilir - boylece bir sonraki adim asla exception atmaz.
    /// </summary>
    private static double ResolveSteppableValue(string text, double min, double max, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw)
            && !double.IsNaN(raw) && !double.IsInfinity(raw))
        {
            return Math.Clamp(raw, min, max);
        }

        return defaultValue;
    }

    /// <summary>[NumberBox - Polish] Adim SONUCUNU, orijinal metnin ondalik ayiricisini/basamak sayisini KORUYARAK bicimlendirir (80,5 + 1 → 81,5; ayirici yoksa tam sayi). MaxResultsTextBox'ta ayirici hicbir zaman olusmaz (StepMaxResults bu metodu kullanmaz).</summary>
    private static string FormatSteppedNumber(double value, string originalText)
    {
        var separatorIndex = originalText.IndexOfAny(new[] { ',', '.' });
        if (separatorIndex < 0)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        var separatorChar = originalText[separatorIndex];
        var decimalDigits = Math.Max(1, originalText.Length - separatorIndex - 1);
        var formatted = value.ToString("0." + new string('0', decimalDigits), CultureInfo.InvariantCulture);
        return separatorChar == ',' ? formatted.Replace('.', ',') : formatted;
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

    /// <summary>[Sonuç sınırı] Gecersiz "en fazla sonuç": odak hatali alana doner, sade (modal olmayan) bir mesaj gosterilir - ThresholdValidationText ile AYNI desen. Mesaj cagiran tarafindan secilir (bkz. SearchButton_Click - "ust siniri asan" ile "diger gecersiz" durumlar AYRI metinler kullanir).</summary>
    private void ShowMaxResultsValidationError(string message)
    {
        MaxResultsValidationText.Text = message;
        MaxResultsValidationText.Visibility = Visibility.Visible;
        MaxResultsTextBox.Focus();
        MaxResultsTextBox.SelectAll();
    }

    private void HideMaxResultsValidationError()
    {
        MaxResultsValidationText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// [Model yükleme - arka plana taşıma] Önceden `new ClipEmbedder(modelPath)`
    /// UI thread'inde SENKRON çalışıyordu - CLIP ONNX modelinin ilk yüklenmesi
    /// (ilk arama veya ilk indeksleme) fark edilir bir donmaya yol açabiliyordu,
    /// ki bu sırada yeni işlem ilerleme paneli de animasyon YAPAMAZDI (aynı UI
    /// thread bloklu). Model oluşturma artık `Task.Run` ile arka planda çalışır.
    /// `_embedderInitLock` (SemaphoreSlim) aynı anda iki model oluşturulmasını
    /// engeller - iki çağıran (Search/UpdateIndex) zaten `IsBusy` ile karşılıklı
    /// dışlanır, ama bu ikinci bir savunma katmanıdır. Başarılı örnek `_embedder`
    /// alanında saklanır ve bir sonraki çağrıda (kilit gerekmeden) tekrar
    /// kullanılır. Hata mesajları eski senkron sürümle BİREBİR aynı.
    /// </summary>
    private async Task<(bool Success, string Error)> TryEnsureEmbedderAsync()
    {
        if (_embedder is not null)
        {
            return (true, string.Empty);
        }

        await _embedderInitLock.WaitAsync();
        try
        {
            // Kilidi beklerken baska bir cagiran zaten olusturmus olabilir.
            if (_embedder is not null)
            {
                return (true, string.Empty);
            }

            var modelPath = ResolveModelPath();
            if (modelPath is null)
            {
                return (false,
                    "CLIP ONNX model dosyası bulunamadı (models\\clip-vision-b16-openai.onnx). "
                    + "Model dosyasının uygulama klasöründeki 'models' alt klasöründe olduğundan emin olun.");
            }

            try
            {
                _embedder = await Task.Run(() => new ClipEmbedder(modelPath));
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Model yüklenirken hata oluştu:\n{ex.Message}");
            }
        }
        finally
        {
            _embedderInitLock.Release();
        }
    }

    /// <summary>
    /// [İşlem ilerleme paneli - deney] Bir kullanıcı işleminin (arama/indeksleme)
    /// EN DIŞ sınırında BİR KEZ çağrılır - mevcut `SetBusy(true)` ile aynı yerde.
    /// İlk aşama metnini hemen yazar (bkz. <see cref="SetOperationStage"/>) ve
    /// 250ms'lik göster-gecikmesini sıfırdan başlatır; panel bu süre boyunca
    /// GÖRÜNMEZ kalır (`OperationOverlay.Visibility` hâlâ Collapsed) - işlem
    /// bu sürede biterse <see cref="EndOperation"/> zamanlayıcıyı durdurur ve
    /// panel HİÇ görünmez.
    /// </summary>
    private void BeginOperation(string title, string? description = null)
    {
        _operationShowTimer.Stop();
        OperationOverlay.Visibility = Visibility.Collapsed;
        SetOperationStage(title, description);
        _operationShowTimer.Start();
    }

    /// <summary>
    /// [İşlem ilerleme paneli - deney] Tek bir işlem İÇİNDE aşama değiştiğinde
    /// (ör. "İndeks güncelliği kontrol ediliyor…" → "Desenler indeksleniyor…")
    /// çağrılır - zamanlayıcıya/görünürlüğe DOKUNMAZ, yalnızca kartın metin/
    /// ilerleme alanlarını günceller. Böylece panel zaten açıksa aşama geçişi
    /// kapanıp-tekrar-açılma (titreşme) YAPMAZ; henüz gecikme sürüyorsa panel
    /// ilk kez göründüğünde o anki GÜNCEL aşama metniyle açılır.
    /// </summary>
    private void SetOperationStage(
        string title, string? description = null, bool indeterminate = true,
        string? progressText = null, double progressValue = 0)
    {
        OperationTitleText.Text = title;

        OperationDescriptionText.Text = description ?? string.Empty;
        OperationDescriptionText.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        OperationProgressBar.IsIndeterminate = indeterminate;
        OperationProgressBar.Value = indeterminate ? 0 : progressValue;

        OperationProgressText.Text = progressText ?? string.Empty;
        OperationProgressText.Visibility = string.IsNullOrEmpty(progressText) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OperationShowTimer_Tick(object? sender, EventArgs e)
    {
        _operationShowTimer.Stop();
        OperationOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// [İşlem ilerleme paneli - deney] Her `BeginOperation`'ın EŞLEŞTİĞİ, işlemin
    /// EN DIŞ `finally` bloğunda çağrılır (mevcut `SetBusy(false)` ile aynı yerde,
    /// başarı/hata/erken-return HER yolda). Zamanlayıcıyı durdurur (henüz 250ms
    /// dolmadıysa panel hiç görünmeden iptal edilmiş olur), paneli kapatır VE
    /// tüm metin/ilerleme alanlarını boşaltır - bir sonraki işlemde veya "Yeni
    /// Arama" sonrasında önceki aşamaya ait metin asla sızmaz.
    /// </summary>
    private void EndOperation()
    {
        _operationShowTimer.Stop();
        OperationOverlay.Visibility = Visibility.Collapsed;
        OperationTitleText.Text = string.Empty;
        OperationDescriptionText.Text = string.Empty;
        OperationDescriptionText.Visibility = Visibility.Collapsed;
        OperationProgressBar.IsIndeterminate = true;
        OperationProgressBar.Value = 0;
        OperationProgressText.Text = string.Empty;
        OperationProgressText.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// [Sorgu kilidi] Tek gercek "mesgul mu" kaynagi _busyDepth'tir - bkz. IsBusy.
    /// SetBusy(true)/(false) cagrilari her zaman ESLESIR (try/finally ile), bu
    /// yuzden derinlik normalde 0/1 arasinda gidip gelir; ama olasi bir nested
    /// cagride (ör. ileride bir alt-islem de SetBusy kullanirsa) ic taraf bittiginde
    /// disarinin korumasini YANLISLIKLA kaldirmaz - yalnizca EN DISTAKI SetBusy(false)
    /// derinligi sifira indirdiginde arayuz gercekten "mesgul degil" olur.
    /// NewSearchButton ve MaxResultsTextBox buraya [Sorgu kilidi] turunda eklendi -
    /// onceden yalnizca gorsel olarak degil, IsBusy kontrolu ile ilgili olay
    /// isleyicilerinde de (bkz. NewSearchButton_Click, QueryDropZone_*) korunuyorlar.
    /// </summary>
    private void SetBusy(bool busy)
    {
        _busyDepth = Math.Max(0, _busyDepth + (busy ? 1 : -1));
        var isBusy = IsBusy;

        SelectFolderButton.IsEnabled = !isBusy;
        UpdateIndexButton.IsEnabled = !isBusy;
        SearchButton.IsEnabled = !isBusy;
        NewSearchButton.IsEnabled = !isBusy;
        ThresholdTextBox.IsEnabled = !isBusy;
        MaxResultsTextBox.IsEnabled = !isBusy;
        ThresholdUpButton.IsEnabled = !isBusy;
        ThresholdDownButton.IsEnabled = !isBusy;
        MaxResultsUpButton.IsEnabled = !isBusy;
        MaxResultsDownButton.IsEnabled = !isBusy;
        AutoIndexCheckBox.IsEnabled = !isBusy;
        SetDefaultButton.IsEnabled = !isBusy && _productFolder is not null && _directoryOrigin != DirectoryOrigin.UserOverride;
        ClearDefaultButton.IsEnabled = !isBusy;
        ProblemFilesButton.IsEnabled = !isBusy;
        // [Klasör adresi elle girme] BİLEREK IsEnabled DEĞİL - disabled bir WPF
        // TextBox'ta metin SEÇİLEMEZ/KOPYALANAMAZ (hit-testing kapanır). IsReadOnly
        // yalnızca DÜZENLEMEYİ engeller; seçim/Ctrl+C/sağ-tık-kopyala meşgulken de
        // ÇALIŞMAYA DEVAM EDER (bkz. talimat "mevcut adresi seçme ve kopyalama
        // mümkün kalsın").
        FolderPathTextBox.IsReadOnly = isBusy;
        Cursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
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
