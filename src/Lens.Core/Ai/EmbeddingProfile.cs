namespace Lens.Core.Ai;

/// <summary>
/// Bir index dosyasindaki embedding'lerin HANGI kosullarda uretildigini tam
/// olarak tanimlayan degismez (immutable) kimlik. Index dosyasina AYNEN
/// yazilir ve her yuklemede calisan profille KARSILASTIRILIR (bkz.
/// docs/DECISIONS.md #95): bu alanlardan HERHANGI biri farkliysa eski
/// embedding'ler kesinlikle KULLANILMAZ, index tamamen yeniden uretilir.
///
/// Gerekce: onceki surumde index kaydi yalnizca dosya kimligi + vektor
/// tutuyordu; model veya on isleme degistiginde eski vektorler SESSIZCE
/// kullanilmaya devam ediyor ve farkli modellerin vektorleri ayni dosyada
/// karisabiliyordu. Bu tip o riski yapisal olarak ortadan kaldirir.
///
/// System.Text.Json ile serialize/deserialize edilir - alan adlari kalici bir
/// dosya sozlesmesidir. Yeniden adlandirilirsa eski dosya "uyumsuz" sayilir ve
/// yeniden indeksleme tetiklenir (veri kaybi olmaz, ama gereksiz tarama olur).
/// </summary>
/// <param name="ModelId">Model kimligi, orn. "facebook/dinov2-base".</param>
/// <param name="ModelRevision">Resmi kaynaktaki kesin revision/commit - ayni ada sahip farkli agirliklari ayirt eder.</param>
/// <param name="ModelSha256">Kullanilan .onnx dosyasinin TAM SHA-256'si. Dosya degistirilmisse boyut/isim ayni olsa bile yakalanir.</param>
/// <param name="PreprocessingVersion">On isleme sozlesmesinin surumu (resize/crop/mean/std). Sabitler degisirse bu da degismeli.</param>
/// <param name="EmbeddingDimension">Vektor boyutu (orn. 768).</param>
/// <param name="FeatureType">Modelden alinan ozellik turu, orn. "CLS".</param>
/// <param name="CropStrategy">Crop/tile stratejisi, orn. "SingleCenterCrop224" (tek global embedding).</param>
/// <param name="Normalization">Vektor normalizasyonu, orn. "L2".</param>
/// <param name="IndexSchemaVersion">Index dosyasinin yapisal sema surumu.</param>
public sealed record EmbeddingProfile(
    string ModelId,
    string ModelRevision,
    string ModelSha256,
    string PreprocessingVersion,
    int EmbeddingDimension,
    string FeatureType,
    string CropStrategy,
    string Normalization,
    int IndexSchemaVersion)
{
    /// <summary>
    /// Kayitli profil ile calisan profilin index'i PAYLASABILECEK kadar ayni
    /// olup olmadigi. Tek karar noktasi <see cref="DescribeMismatch"/>'tir -
    /// iki ayri alan listesi tutulmaz ki biri guncellenip digeri unutulmasin.
    /// </summary>
    public bool MatchesForIndexReuse(EmbeddingProfile? stored) => DescribeMismatch(stored) is null;

    /// <summary>
    /// Uyumsuzlugun INSAN OKUNABILIR nedenini dondurur; uyumluysa null.
    /// Loglama ve kullaniciya gosterilen "yeniden indeksleme gerekiyor"
    /// mesajinda gerekce olarak kullanilir.
    /// </summary>
    public string? DescribeMismatch(EmbeddingProfile? stored)
    {
        if (stored is null)
        {
            return "kayıtlı profil yok";
        }

        if (stored.IndexSchemaVersion != IndexSchemaVersion)
        {
            return $"index şema sürümü ({stored.IndexSchemaVersion} → {IndexSchemaVersion})";
        }

        if (!OrdinalEquals(stored.ModelId, ModelId))
        {
            return $"model kimliği ({stored.ModelId} → {ModelId})";
        }

        if (!OrdinalEquals(stored.ModelRevision, ModelRevision))
        {
            return "model revision";
        }

        if (!OrdinalEquals(stored.ModelSha256, ModelSha256))
        {
            return "model dosyası SHA-256";
        }

        if (!OrdinalEquals(stored.PreprocessingVersion, PreprocessingVersion))
        {
            return $"ön işleme sürümü ({stored.PreprocessingVersion} → {PreprocessingVersion})";
        }

        if (stored.EmbeddingDimension != EmbeddingDimension)
        {
            return $"embedding boyutu ({stored.EmbeddingDimension} → {EmbeddingDimension})";
        }

        if (!OrdinalEquals(stored.FeatureType, FeatureType))
        {
            return $"özellik türü ({stored.FeatureType} → {FeatureType})";
        }

        if (!OrdinalEquals(stored.CropStrategy, CropStrategy))
        {
            return $"crop stratejisi ({stored.CropStrategy} → {CropStrategy})";
        }

        if (!OrdinalEquals(stored.Normalization, Normalization))
        {
            return $"normalizasyon ({stored.Normalization} → {Normalization})";
        }

        return null;
    }

    /// <summary>SHA-256 metni buyuk/kucuk harf farkiyla yazilabilir; diger alanlar da bas/son bosluk farkina takilmasin diye ayni yardimci kullanilir.</summary>
    private static bool OrdinalEquals(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
