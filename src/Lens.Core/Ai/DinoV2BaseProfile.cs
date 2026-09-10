namespace Lens.Core.Ai;

/// <summary>
/// [PILOT] DINOv2 ViT-B/14 (facebook/dinov2-base) icin model-spesifik
/// sabitler. Bu bir PILOT yapilandirmasidir - production model karari
/// DEGILDIR (bkz. docs/DECISIONS.md, docs/MODEL_CARD.md).
///
/// Bu sabitler MODEL DOSYASINA ERISMEDEN bilinebilir; bu onemlidir cunku
/// uygulama, model henuz yuklenmeden once de (klasor secimi/durum gosterimi)
/// index'in HANGI klasorde oldugunu ve baslangic esiginin ne olacagini
/// bilmek zorundadir. Yalnizca <see cref="EmbeddingProfile.ModelSha256"/>
/// dosyanin kendisinden hesaplanir (bkz. <see cref="CreateProfile"/>).
/// </summary>
public static class DinoV2BaseProfile
{
    /// <summary>Resmi Hugging Face model kimligi.</summary>
    public const string ModelId = "facebook/dinov2-base";

    /// <summary>Resmi Hugging Face deposundaki kesin revision (commit) - agirliklarin bu surumunden export edildi.</summary>
    public const string ModelRevision = "f9e44c814b77203eaa57a6bdbbd535f21ede1415";

    /// <summary>DINOv2 ViT-B/14 gizli boyutu = CLS token uzunlugu.</summary>
    public const int EmbeddingDimension = 768;

    /// <summary>Modelden alinan ozellik: son katmanin CLS token'i (DINOv2'de projection head YOKTUR).</summary>
    public const string FeatureType = "CLS";

    /// <summary>Vektor normalizasyonu - grafige DAHIL DEGILDIR, calisma zamaninda uygulanir (bkz. <see cref="DinoV2Embedder"/>).</summary>
    public const string Normalization = "L2";

    /// <summary>
    /// Index dosyasinin yapisal sema surumu. 1 = eski, profilsiz duz dizi
    /// (CLIP `.lens/index.json`); 2 = profil zarfli belge (bkz.
    /// <see cref="Lens.Core.Indexing.IndexDocument"/>).
    /// </summary>
    public const int IndexSchemaVersion = 2;

    /// <summary>
    /// Bu profile ait index'in `.lens/indexes/` altindaki alt klasoru. Model
    /// veya sema degisirse BU ADIN da degismesi beklenir - boylece iki farkli
    /// modelin index'i ayni dosyayi hicbir kosulda paylasmaz.
    /// </summary>
    public const string IndexFolderName = "dinov2-base-v1";

    /// <summary>Uygulama klasorundeki `models\` altinda beklenen ONNX dosya adi.</summary>
    public const string ModelFileName = "dinov2-base.onnx";

    /// <summary>ONNX girdi tensorunun adi - export betigi (benchmark/export_dinov2_onnx.py) ile BIREBIR ayni olmalidir.</summary>
    public const string OnnxInputName = "pixel_values";

    /// <summary>ONNX cikti tensorunun adi - export betigi ile BIREBIR ayni olmalidir.</summary>
    public const string OnnxOutputName = "image_embeds";

    /// <summary>
    /// [PILOT ESIGI] Baslangic "Minimum benzerlik (%)" degeri. CLIP'in %80'i
    /// DINOv2 dagilimina UYMAZ - tam veri olcumunde %80, DINOv2'de dogru
    /// eslesmelerin yalnizca ~%68'ini listede birakiyordu (CLIP'te ~%93).
    /// Olcum %55-60 bandini oneriyor; kesin deger gercek katalogdaki kabul
    /// edilebilir yanlis pozitif oranina gore kalibre edilmelidir (bkz.
    /// docs/DECISIONS.md #95, Not Yet Decided #16). GECICIDIR.
    /// </summary>
    public const double DefaultThresholdPercent = 55;

    /// <summary>
    /// Tam profili, verilen model dosyasi hash'i ile olusturur. On isleme
    /// surumu/crop stratejisi <see cref="ImagePreprocessingProfile.DinoV2"/>
    /// profilinden TUREVDIR - iki yerde ayri ayri yazilmaz ki biri degisip
    /// digeri unutulmasin.
    /// </summary>
    public static EmbeddingProfile CreateProfile(string modelSha256) => new(
        ModelId: ModelId,
        ModelRevision: ModelRevision,
        ModelSha256: modelSha256,
        PreprocessingVersion: ImagePreprocessingProfile.DinoV2.Version,
        EmbeddingDimension: EmbeddingDimension,
        FeatureType: FeatureType,
        CropStrategy: ImagePreprocessingProfile.DinoV2.CropStrategy,
        Normalization: Normalization,
        IndexSchemaVersion: IndexSchemaVersion);
}
