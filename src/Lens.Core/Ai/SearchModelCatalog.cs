namespace Lens.Core.Ai;

/// <summary>Kullanicinin Arama Ayarlari panelinden secebilecegi model.</summary>
public enum SearchModelKind
{
    /// <summary>Varsayilan ve KANITLANMIS profil - kullanicinin ~5.000 gorsellik gercek katalog denemesinde dogru deseni ilk sirada buldu.</summary>
    DinoV2Base,

    /// <summary>Standart, TEK global embedding ureten CLIP. Desen pilotundaki alti gorunumlu yontem BURAYA TASINMADI.</summary>
    ClipStandard,
}

/// <summary>Goruntunun nasil degerlendirilecegi.</summary>
public enum ImageColorMode
{
    /// <summary>Modelin kendi resmi renkli on islemesi - DINO icin mevcut, dogrulanmis yol.</summary>
    Color,

    /// <summary>Hem katalog hem sorgu griye cevrilir; tek kanal, modelin bekledigi uc kanala esit kopyalanir.</summary>
    Grayscale,
}

/// <summary>
/// Bir model + renk modu kombinasyonunun TAM tanimi: hangi dosya, hangi on
/// isleme, hangi index klasoru, hangi baslangic esigi.
///
/// Dort kombinasyon vardir (2 model x 2 renk modu) ve her biri KENDI
/// index'ini kullanir - embedding'ler hicbir kosulda karismaz.
/// </summary>
/// <param name="Kind">Model.</param>
/// <param name="ColorMode">Renk modu.</param>
/// <param name="DisplayName">Arayuzde gorunen ad.</param>
/// <param name="ModelFileName">`models\` altinda beklenen ONNX dosyasi.</param>
/// <param name="ModelId">Resmi model kimligi.</param>
/// <param name="ModelRevision">Resmi kaynaktaki kesin revision.</param>
/// <param name="EmbeddingDimension">Vektor boyutu (DINOv2-B 768, CLIP 512).</param>
/// <param name="FeatureType">Modelden alinan ozellik.</param>
/// <param name="OnnxOutputName">ONNX cikti tensorunun adi.</param>
/// <param name="Preprocessing">Renkli on isleme profili; gri modda bu profil temel alinip gri bayragi eklenir.</param>
/// <param name="IndexFolderName">`.lens/indexes/` altindaki klasor adi.</param>
/// <param name="DefaultThresholdPercent">Merkezleme KAPALI iken baslangic esigi.</param>
public sealed record SearchModelProfile(
    SearchModelKind Kind,
    ImageColorMode ColorMode,
    string DisplayName,
    string ModelFileName,
    string ModelId,
    string ModelRevision,
    int EmbeddingDimension,
    string FeatureType,
    string OnnxOutputName,
    ImagePreprocessingProfile Preprocessing,
    string IndexFolderName,
    double DefaultThresholdPercent)
{
    /// <summary>ONNX girdi tensorunun adi - her iki modelde de ayni.</summary>
    public const string OnnxInputName = "pixel_values";

    /// <summary>
    /// Index sema surumu. DINO RENKLI icin BILEREK 2'dir: kullanicinin mevcut
    /// ~5.000 gorsellik index'i gecerli kalsin ve gereksiz yere yeniden
    /// uretilmesin. Diger uc kombinasyon yeni oldugu icin ayni surumu
    /// kullanir - onlarin index'i zaten yoktur.
    /// </summary>
    public int IndexSchemaVersion => 2;

    /// <summary>
    /// On isleme kimligi. Gri modda renkli profilden FARKLI bir kimlik
    /// tasir - boylece gri ve renkli embedding'ler ayni index'te
    /// karisamaz ve profil karsilastirmasi bunu yakalar.
    /// </summary>
    public string PreprocessingVersion => ColorMode == ImageColorMode.Grayscale
        ? Preprocessing.Version + "+grayscale-v1"
        : Preprocessing.Version;

    /// <summary>Index profiline yazilan normalizasyon kimligi.</summary>
    public string Normalization => "L2";

    /// <summary>Bu profilin index'te sakladigi crop stratejisi - tek global embedding.</summary>
    public string CropStrategy => Preprocessing.CropStrategy;

    /// <summary>
    /// Kullanici ayarlarinda esik saklamak icin kararli anahtar.
    /// Merkezleme skor olcegini degistirdigi icin ONUN DA parcasidir.
    /// </summary>
    public string ThresholdKey(bool centering) =>
        $"{Kind}|{ColorMode}|{(centering ? "centered" : "raw")}";

    /// <summary>
    /// Tam <see cref="EmbeddingProfile"/>. Model dosyasinin gercek SHA-256'si
    /// disaridan verilir (hesabi pahalidir, oturum basina bir kez yapilir).
    ///
    /// DINO RENKLI icin uretilen profil, onceki pilotun urettigiyle BIREBIR
    /// AYNI olmalidir - aksi halde kullanicinin mevcut index'i uyumsuz
    /// sayilir ve 5.000 gorsel bosuna yeniden indekslenir (bkz. AiProof
    /// Grup Q).
    /// </summary>
    public EmbeddingProfile CreateEmbeddingProfile(string modelSha256) => new(
        ModelId: ModelId,
        ModelRevision: ModelRevision,
        ModelSha256: modelSha256,
        PreprocessingVersion: PreprocessingVersion,
        EmbeddingDimension: EmbeddingDimension,
        FeatureType: FeatureType,
        CropStrategy: CropStrategy,
        Normalization: Normalization,
        IndexSchemaVersion: IndexSchemaVersion);
}

/// <summary>
/// Desteklenen dort model/renk kombinasyonu.
///
/// KRITIK: DINO renkli girdisinin her alani, onceki tek-modelli pilotun
/// <c>DinoV2BaseProfile</c> degerleriyle BIREBIR AYNIDIR (model kimligi,
/// revision, on isleme surumu, boyut, ozellik turu, crop, normalizasyon,
/// sema surumu ve index klasoru). Bu, kullanicinin mevcut ~5.000 gorsellik
/// DINO index'inin gecerli kalmasinin tek garantisidir.
/// </summary>
public static class SearchModelCatalog
{
    /// <summary>Ilk acilista ve bilinmeyen/gecersiz ayarda kullanilan KANITLANMIS profil.</summary>
    public static readonly SearchModelProfile DinoColor = new(
        Kind: SearchModelKind.DinoV2Base,
        ColorMode: ImageColorMode.Color,
        DisplayName: "DINOv2 Base — Renkli",
        ModelFileName: DinoV2BaseProfile.ModelFileName,
        ModelId: DinoV2BaseProfile.ModelId,
        ModelRevision: DinoV2BaseProfile.ModelRevision,
        EmbeddingDimension: DinoV2BaseProfile.EmbeddingDimension,
        FeatureType: DinoV2BaseProfile.FeatureType,
        OnnxOutputName: DinoV2BaseProfile.OnnxOutputName,
        Preprocessing: ImagePreprocessingProfile.DinoV2,
        IndexFolderName: DinoV2BaseProfile.IndexFolderName,
        DefaultThresholdPercent: DinoV2BaseProfile.DefaultThresholdPercent);

    /// <summary>DINOv2-Base, gri tonlamali. Renkli profilden AYRI index ve AYRI on isleme kimligi kullanir.</summary>
    public static readonly SearchModelProfile DinoGray = DinoColor with
    {
        ColorMode = ImageColorMode.Grayscale,
        DisplayName = "DINOv2 Base — Gri tonlamalı",
        IndexFolderName = "dinov2-base-gray-v1",

        // [Gecici deger - olcume dayali] Gri, renk bilgisini kaldirdigi icin
        // skor dagilimi asagi kayar. Onceki CLIP deneyinde gri tek basina
        // genel basariyi DUSURDU; bu yuzden esik, dogru eslesmeleri gereksiz
        // elememek uzere renkliden 5 puan dusuk secildi. KALIBRASYON DEGILDIR
        // (bkz. docs/MULTI_MODEL_SEARCH.md "Eşikler").
        DefaultThresholdPercent = 50,
    };

    /// <summary>Standart CLIP - TEK global embedding. Desen pilotunun alti gorunumlu yontemi BURAYA TASINMADI.</summary>
    public static readonly SearchModelProfile ClipColor = new(
        Kind: SearchModelKind.ClipStandard,
        ColorMode: ImageColorMode.Color,
        DisplayName: "CLIP Standart — Renkli",
        ModelFileName: "clip-vision-b16-openai.onnx",
        ModelId: "openai/clip-vit-base-patch16",
        ModelRevision: "57c216476eefef5ab752ec549e440a49ae4ae5f3",
        EmbeddingDimension: ClipEmbedder.EmbeddingDimension,
        FeatureType: "ProjectedImageEmbeds",
        OnnxOutputName: "image_embeds",
        Preprocessing: ImagePreprocessingProfile.Clip,
        IndexFolderName: "clip-standard-rgb-v1",

        // CLIP'in tarihsel varsayilani (bkz. SimilarityThreshold.DefaultPercent).
        DefaultThresholdPercent: SimilarityThresholdDefaults.ClipPercent);

    /// <summary>Standart CLIP, gri tonlamali. Renkli profilden AYRI index ve AYRI on isleme kimligi kullanir.</summary>
    public static readonly SearchModelProfile ClipGray = ClipColor with
    {
        ColorMode = ImageColorMode.Grayscale,
        DisplayName = "CLIP Standart — Gri tonlamalı",
        IndexFolderName = "clip-standard-gray-v1",

        // [Gecici deger] CLIP renkli %80'dir; gri modda skorlar asagi kaydigi
        // icin ayni oranda dusuruldu. Olcume dayali gecici degerdir.
        DefaultThresholdPercent = 70,
    };

    /// <summary>Dort kombinasyonun tamami.</summary>
    public static readonly IReadOnlyList<SearchModelProfile> All = new[]
    {
        DinoColor, DinoGray, ClipColor, ClipGray,
    };

    /// <summary>
    /// Verilen secime karsilik gelen profil. Bilinmeyen/gecersiz kombinasyon
    /// icin KANITLANMIS varsayilana (DINO renkli) guvenle doner - kullanici
    /// bozuk bir ayar dosyasi yuzunden uygulamayi kullanamaz duruma DUSMEZ.
    /// </summary>
    public static SearchModelProfile Resolve(SearchModelKind kind, ImageColorMode colorMode) =>
        All.FirstOrDefault(p => p.Kind == kind && p.ColorMode == colorMode) ?? DinoColor;

    /// <summary>Ayar dosyasindaki metinden guvenle cozer; taninmayan deger varsayilana doner.</summary>
    public static SearchModelProfile ResolveOrDefault(string? modelText, string? colorText)
    {
        var kind = Enum.TryParse<SearchModelKind>(modelText, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : SearchModelKind.DinoV2Base;

        var color = Enum.TryParse<ImageColorMode>(colorText, ignoreCase: true, out var parsedColor)
            ? parsedColor
            : ImageColorMode.Color;

        return Resolve(kind, color);
    }
}

/// <summary>Model bazli baslangic esiklerinin tek kaynagi - sayilar koda dagilmasin.</summary>
public static class SimilarityThresholdDefaults
{
    /// <summary>CLIP'in tarihsel varsayilani (bkz. Lens.Core.Search.SimilarityThreshold.DefaultPercent).</summary>
    public const double ClipPercent = 80;
}
