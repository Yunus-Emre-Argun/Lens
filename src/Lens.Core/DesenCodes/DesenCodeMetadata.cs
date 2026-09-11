namespace Lens.Core.DesenCodes;

/// <summary>Bir dosyanin desen kodu kaydinin son durumu.</summary>
public enum DesenCodeEntryState
{
    /// <summary>Servis gecerli bir kod dondurdu.</summary>
    Found,

    /// <summary>Servis calisti ve bu dosya icin kod OLMADIGINI bildirdi. Sonraki acik yenilemede tekrar denenir.</summary>
    NotFound,
}

/// <summary>
/// Tek bir gorselin desen kodu kaydi.
///
/// <see cref="Code"/> BILEREK string'tir: kodlarda bastaki sifirlar
/// anlamlidir ("00123" ≠ 123) ve kesin uzunluk dogrulanmamistir - sayiya
/// cevirmek veya sabit hane kisiti koymak veri kaybettirir.
/// </summary>
public sealed class DesenCodeEntry
{
    /// <summary>Katalog kokune gore goreli yol - alt klasorlerde ayni adli dosyalari ayirt eden TEK anahtar.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Servise gercekte gonderilen ad (uzantili veya uzantisiz) - hangi bicimin eslestigini kaydeder.</summary>
    public string QueriedFileName { get; set; } = string.Empty;

    /// <summary>Desen kodu. <see cref="State"/> = NotFound ise null.</summary>
    public string? Code { get; set; }

    /// <summary>Kaydin son durumu - "kod yok" ile "kod var" ayrimi burada tutulur.</summary>
    public DesenCodeEntryState State { get; set; }

    /// <summary>Bu kaydin en son BASARIYLA guncellendigi an (UTC). Basarisiz yenileme bunu DEGISTIRMEZ.</summary>
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// Katalog yanindaki ortak desen kodu metadata dosyasinin bicimi.
///
/// Embedding indekslerinden (<c>.lens/index.json</c>,
/// <c>.lens/indexes/*</c>) TAMAMEN AYRIDIR: modelden bagimsizdir, arama
/// skorlarini etkilemez ve model/on isleme degisiminde yeniden sorgulama
/// GEREKTIRMEZ. Gorsellerle birlikte tasindiginda calismaya devam eder.
/// </summary>
public sealed class DesenCodeMetadata
{
    /// <summary>Bu dosyanin bilinen tek surumu. Farkli bir deger = bilinmeyen sema, dosya KULLANILMAZ.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Dosyanin sema surumu. <see cref="CurrentSchemaVersion"/> disinda bir deger = bilinmeyen sema, dosya KULLANILMAZ.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Kodlarin hangi servisten alindigi (adres + metot). Destek/izlenebilirlik icindir; kimlik bilgisi ICERMEZ.</summary>
    public string? SourceService { get; set; }

    /// <summary>En son BASARILI toplu guncellemenin bittigi an (UTC).</summary>
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; set; }

    /// <summary>Dosya bazinda kod kayitlari. Anahtar <see cref="DesenCodeEntry.RelativePath"/>'tir.</summary>
    public List<DesenCodeEntry> Entries { get; set; } = new();
}
