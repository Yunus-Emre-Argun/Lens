namespace Lens.Core.Indexing;

/// <summary>
/// ImageIndex.DetectChanges() sonucu - ucuz (yalnizca dosya metadata'si
/// karsilastirilir, hicbir gorsel embed edilmez) bir "guncel mi?" kontrolu.
/// Search-before-refresh (Faz 4B) bunu "Ara" oncesi tetikler.
/// </summary>
public sealed record ChangeSummary(
    int NewCount,
    int ChangedCount,
    int RemovedCount,
    int UnchangedCount,
    string? ScanError,
    string? IndexResetReason = null)
{
    /// <summary>
    /// [Profil-dogrulamali index] Index profili uyumsuz/bozuk oldugu icin tam
    /// yeniden olusturma gerekiyorsa, kayitli hicbir dosya "unchanged"
    /// sayilamaz - bu durumda mevcut dosyalarin TAMAMI NewCount'a yazilir,
    /// dolayisiyla HasChanges zaten true olur. Bu alan yalnizca NEDENI
    /// tasir (kullaniciya gosterilecek aciklama icin).
    /// </summary>
    public bool HasChanges => NewCount > 0 || ChangedCount > 0 || RemovedCount > 0;
}
