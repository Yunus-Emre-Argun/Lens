using Lens.Core.Logging;

namespace Lens.Core.Indexing;

/// <summary>Bir index dosyasinin yuklenme sonucu - cagiran tarafin kullaniciya DOGRU mesaji gosterebilmesi icin "neden bos?" sorusunu ayirt eder.</summary>
public enum IndexLoadOutcome
{
    /// <summary>Dosya hic yok - normal "ilk kullanim" durumu.</summary>
    Missing,

    /// <summary>Dosya okundu, gecerli ve calisan profille UYUMLU.</summary>
    Loaded,

    /// <summary>Dosya var ama bozuk/yarim/gecersiz icerik (bkz. docs/DECISIONS.md #56 - "hepsi ya da hicbiri").</summary>
    Corrupt,

    /// <summary>Dosya teknik olarak okunabilir ama BASKA bir model/on isleme profiliyle uretilmis - embedding'ler KULLANILAMAZ (bkz. docs/DECISIONS.md #95).</summary>
    ProfileMismatch,
}

/// <summary>Yukleme sonucu. <see cref="Entries"/>, <see cref="Outcome"/> "Loaded" DISINDA her zaman bostur - uyumsuz/bozuk veri hicbir kosulda aramaya verilmez.</summary>
public sealed record IndexLoadResult(IndexLoadOutcome Outcome, List<ImageIndexEntry> Entries, string? Reason);

/// <summary>
/// Index'in NEREDE ve HANGI bicimde saklandigini soyutlar. Amaci, tarama/
/// guncelleme/kilit/atomic-yazma guvenilirlik mantiginin (bkz.
/// <see cref="ImageIndex"/>) her model icin YENIDEN YAZILMASINI onlemektir:
/// yalnizca depolama degisir, davranis tek yerde kalir.
///
/// Iki uygulama vardir: <see cref="LegacyClipIndexStore"/> (eski, profilsiz
/// CLIP dosyasi - degistirilmez) ve <see cref="ProfiledIndexStore"/> (model
/// profiline ozel klasor + profil dogrulamasi).
/// </summary>
public interface IIndexStore
{
    /// <summary>Loglarda/durum mesajlarinda kullanilan kisa ad, orn. "DINOv2-Base (dinov2-base-v1)".</summary>
    string Description { get; }

    /// <summary>Gecerli bir kaydin embedding uzunlugu. Farkli uzunluk = bozuk/uyumsuz kayit.</summary>
    int ExpectedEmbeddingDimension { get; }

    /// <summary>Index dosyasinin bulundugu klasor. Side-effect-free: OGRENMEK klasoru OLUSTURMAZ.</summary>
    string IndexDirectory(string productDirectory);

    string IndexFilePath(string productDirectory);

    /// <summary>Tek-yazarli exclusive kilit dosyasi. Her store KENDI klasorunde kilitlenir - iki farkli model birbirinin indekslemesini bloklamaz.</summary>
    string LockFilePath(string productDirectory);

    IndexLoadResult Load(string productDirectory, ILensLogger? logger = null);

    /// <summary>Atomic yazar (temp + replace, bkz. <see cref="Lens.Core.IO.AtomicFileWriter"/>); hedef klasor gerekiyorsa BURADA olusturulur.</summary>
    void Save(string productDirectory, List<ImageIndexEntry> entries);
}
