using Lens.Core.Ai;

namespace Lens.Core.Indexing;

/// <summary>
/// Profil-dogrulamali index dosyasinin (sema surumu &gt;= 2) disk bicimi.
/// Eski CLIP dosyasi (`.lens/index.json`) BUNU KULLANMAZ - orada koke duz bir
/// <c>ImageIndexEntry</c> dizisi yazilir (sema surumu 1) ve o dosya bu
/// pilotta HIC okunmaz/yazilmaz.
///
/// Kayit listesi tek basina yeterli DEGILDIR: hangi model/on isleme ile
/// uretildigi de dosyanin icinde durmalidir, aksi halde model degistiginde
/// eski vektorler sessizce kullanilmaya devam eder (bkz.
/// docs/DECISIONS.md #95).
///
/// Mutable property'ler + parametresiz ctor: System.Text.Json'in bu projedeki
/// varsayilan (attribute'suz) kullanimiyla en az surprizli sekilde
/// serialize/deserialize edilir.
/// </summary>
public sealed class IndexDocument
{
    /// <summary>Yapisal sema surumu. Yuklemede calisan profilin surumuyle karsilastirilir (bkz. <see cref="EmbeddingProfile.IndexSchemaVersion"/>).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Bu dosyadaki embedding'leri ureten profil. null/eksik ise dosya uyumsuz sayilir - "profil yok" bir hata durumudur, varsayilan kabul edilemez.</summary>
    public EmbeddingProfile? EmbeddingProfile { get; set; }

    /// <summary>Dosyadaki embedding kayitlari. Profil dogrulamasi GECMEDEN bu liste kullanilmaz (bkz. <see cref="ProfiledIndexStore.Load"/>).</summary>
    public List<ImageIndexEntry> Entries { get; set; } = new();
}
