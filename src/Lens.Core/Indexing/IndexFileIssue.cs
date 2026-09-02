namespace Lens.Core.Indexing;

public enum FileIssueKind
{
    /// <summary>Uzanti desteklenen bir gorseldi ama dosya decode edilemedi (bozuk/erisilemez).</summary>
    SupportedImageButFailed,

    /// <summary>Bilinen bir gorsel formati ama Lens su an desteklemiyor (decode denenmedi).</summary>
    UnsupportedImageFormat,

    /// <summary>Gorsel degil / taninmayan uzanti (orn. .pdf, .zip, .txt) - urun klasoru esas olarak gorsel icindir, bu yuzden sessizce yok sayilmaz.</summary>
    NonImageFile,
}

/// <summary>Sorunlu tek bir dosya kaydi - FAZ 4C (log) ve FAZ 4D (UI detay listesi) bunu tuketecek.</summary>
public sealed record IndexFileIssue(string FileName, string Extension, FileIssueKind Kind, string Reason);
