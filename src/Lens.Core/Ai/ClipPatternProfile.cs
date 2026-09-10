namespace Lens.Core.Ai;

/// <summary>
/// [PILOT] CLIP desen odakli yapilandirmasi.
///
/// CLIP AGIRLIKLARI DEGISTIRILMEMISTIR - resmi
/// `openai/clip-vit-base-patch16` modeli aynen kullanilir. Degisen yalnizca
/// modelin ETRAFINDAKI katmanlardir:
///
///   1. Kadraj: tam goruntu (merkez crop) + %60 boyutunda 5 ORTUSEN bolge
///      (merkez + 4 kose) -> gorsel basina 6 gorunum.
///   2. Skor: global benzerlik ile en iyi yerel bolge benzerliginin esit
///      agirlikli toplami (yalnizca "en iyi bolge"ye bakmak, tek bir kucuk
///      ortak motifin tum eslesmeyi tasimasina izin verirdi).
///   3. Normalizasyon: her gorunum ayri L2-normalize edilir; arama aninda
///      katalog ortalamasi cikarilir (whitening) - CLIP embedding'leri dar
///      bir koni icinde toplandigi icin bu, dogru eslesme ile rakipler
///      arasindaki ayrimi belirgin sekilde buyutur.
///
/// Bu, "en iyi model" iddiasi DEGILDIR - CLIP'in kendi baseline'ina gore
/// olculmus bir iyilestirmedir (bkz. docs/CLIP_PATTERN_EXPERIMENT.md).
/// </summary>
public static class ClipPatternProfile
{
    /// <summary>Resmi Hugging Face model kimligi - agirliklar DEGISTIRILMEDI.</summary>
    public const string ModelId = "openai/clip-vit-base-patch16";

    /// <summary>Yerel Hugging Face onbellegindeki kesin revision.</summary>
    public const string ModelRevision = "57c216476eefef5ab752ec549e440a49ae4ae5f3";

    /// <summary>Tek bir gorunumun CLIP embedding boyutu.</summary>
    public const int ViewDimension = 512;

    /// <summary>Gorsel basina saklanan gorunum sayisi: 1 global + 5 ortusen bolge.</summary>
    public const int ViewCount = 6;

    /// <summary>Index'te saklanan birlesik vektorun uzunlugu (gorunumler ard arda eklenir).</summary>
    public const int EmbeddingDimension = ViewDimension * ViewCount;

    /// <summary>Ortusen bolgelerin kenar orani - komsu bolgelerle ortusur ki bir motif ikiye bolunup hicbir bolgede butun kalmasin.</summary>
    public const float RegionFraction = 0.60f;

    /// <summary>Skor birlestirmede global (tam goruntu) terimin agirligi.</summary>
    public const double GlobalWeight = 0.5;

    /// <summary>Index profiline yazilan ozellik turu: CLIP'in projeksiyonlu goruntu embedding'i, 6 gorunum (1 global + 5 ortusen bolge).</summary>
    public const string FeatureType = "ProjectedImageEmbeds-x6-global+overlap5";

    /// <summary>Index profiline yazilan crop stratejisi - degisirse index tamamen yeniden uretilir.</summary>
    public const string CropStrategy = "CenterCrop224+Overlap5@0.60";

    /// <summary>Index profiline yazilan normalizasyon: gorunum basina L2 + arama aninda katalog ortalamasi cikarma.</summary>
    public const string Normalization = "L2-per-view+catalog-mean-whitening";

    /// <summary>On isleme sozlesmesinin surumu - kadraj/gorunum uretimi degisirse ARTIRILMALIDIR.</summary>
    public const string PreprocessingVersion = "clip-pattern-center-overlap5-v1";

    /// <summary>
    /// Index sema surumu. 1 = eski profilsiz CLIP dizisi, 2 = DINOv2 profil
    /// zarfi, 3 = bu pilot (coklu gorunum, birlesik vektor).
    /// </summary>
    public const int IndexSchemaVersion = 3;

    /// <summary>Bu profile ait index'in `.lens/indexes/` altindaki alt klasoru.</summary>
    public const string IndexFolderName = "clip-pattern-v1";

    /// <summary>Uygulama klasorundeki `models\` altinda beklenen ONNX dosya adi (mevcut CLIP modeli - yenisi indirilmez).</summary>
    public const string ModelFileName = "clip-vision-b16-openai.onnx";

    /// <summary>ONNX girdi tensorunun adi (mevcut CLIP export'uyla ayni).</summary>
    public const string OnnxInputName = "pixel_values";

    /// <summary>ONNX cikti tensorunun adi (mevcut CLIP export'uyla ayni).</summary>
    public const string OnnxOutputName = "image_embeds";

    /// <summary>
    /// [PILOT ESIGI] Baslangic "Minimum benzerlik (%)" degeri.
    ///
    /// Whitening skor olcegini DEGISTIRIR - CLIP'in eski %80'i ve DINOv2
    /// pilotunun %55'i bu yonteme AYNEN TASINAMAZ. Deger, mevcut uretim
    /// davranisinin "ne kadar filtreliyor" hissini KORUYACAK sekilde
    /// secildi: 2.007 gorsellik katalogda 336 donusum sorgusuyla olculen
    /// tutulma orani
    ///
    ///   bugunku CLIP @ %80 -> dogru hedeflerin %96'si listede kalir
    ///   bu yontem   @ %55 -> dogru hedeflerin %96'si listede kalir
    ///
    /// Bu bir KALIBRASYON DEGILDIR - kabul edilebilir yanlis pozitif orani
    /// gercek katalogda olculmeden kesin deger belirlenemez.
    /// </summary>
    public const double DefaultThresholdPercent = 55;

    /// <summary>Tam profili, verilen model dosyasi hash'i ile olusturur.</summary>
    public static EmbeddingProfile CreateProfile(string modelSha256) => new(
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
