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
    string? ScanError)
{
    public bool HasChanges => NewCount > 0 || ChangedCount > 0 || RemovedCount > 0;
}
