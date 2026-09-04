namespace Lens.Core.Config;

/// <summary>
/// Kullanıcının elle yazdığı/yapıştırdığı ürün klasörü adresinin BİÇİM
/// düzeyinde (disk erişimi GEREKTİRMEYEN) doğrulaması - bkz. MainWindow
/// FolderPathTextBox (elle adres uygulama). Diskteki gerçek varlık/erişim
/// kontrolü (Directory.Exists/File.Exists) BİLEREK burada YAPILMAZ - arayüzü
/// bloke etmemesi için bunu çağıran taraf (UI) arka planda (Task.Run) yapar.
/// </summary>
public static class ProductFolderPathInput
{
    /// <summary>
    /// Baş/son boşluk ve eşleşen dış çift tırnak güvenle temizlenir. Boş girdi
    /// veya göreli bir yol (bkz. <see cref="Path.IsPathRooted"/>) REDDEDİLİR -
    /// göreli bir adres ASLA çalışma dizinine göre başka bir klasöre
    /// YÖNLENDİRİLMEZ (yalnızca kök/rooted bir yol için <see cref="Path.GetFullPath"/>
    /// çağrılır). Başarılı dönüşte <paramref name="normalized"/>, tam (rooted),
    /// gereksiz "." /".." segmentlerinden arındırılmış ve sondaki ayırıcısı
    /// temizlenmiş yoldur - yoldaki boşluklar ve Türkçe karakterler korunur,
    /// hiçbir şekilde değiştirilmez. Hem tam yerel yollar (C:\...) hem de UNC
    /// ağ paylaşımları (\\Sunucu\Paylaşım) desteklenir - ikisi de "rooted"
    /// sayılır.
    /// </summary>
    public static bool TryNormalizeFormat(string? rawInput, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var text = (rawInput ?? string.Empty).Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        if (text.Length == 0)
        {
            error = "Lütfen bir klasör adresi girin.";
            return false;
        }

        if (!Path.IsPathRooted(text))
        {
            error = "Lütfen tam bir klasör adresi girin (ör. C:\\Ürünler veya \\\\Sunucu\\Paylaşım).";
            return false;
        }

        try
        {
            var full = Path.GetFullPath(text);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Kok bir yol (ör. "C:\") TrimEnd sonrasi "C:" olur, hala gecerli -
            // ama tamamen bos KALMAMASI icin (teorik olarak) orijinal 'full' kullanilir.
            normalized = trimmed.Length > 0 ? trimmed : full;
        }
        catch (Exception)
        {
            error = "Geçersiz klasör adresi.";
            return false;
        }

        return true;
    }
}
