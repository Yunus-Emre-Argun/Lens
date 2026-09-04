using System.Windows.Media;

namespace Lens.Desktop;

/// <summary>
/// [Görsel güncelleme - tema turu] Kullanıcının menüden seçebileceği ana
/// pencere zemin/metin renk paleti. Yalnızca UI (Lens.Desktop) katmanında
/// yaşar - Lens.Core'a veya paylaşılan index/ürün klasörüne dokunmaz.
/// UserSettings.Theme alanında bu enum'un adı (ör. "KoyuSepya") string
/// olarak saklanır (bkz. MainWindow.ParseTheme).
/// </summary>
public enum AppTheme
{
    Acik,
    Normal,
    Koyu,
    AcikSepya,
    KoyuSepya,
    Lime,
}

/// <summary>
/// Bir temanın ana pencere zemini üzerinde kullanılacak renk seti. Kart/
/// thumbnail/sorgu-sonuç görsel kutuları ve giriş kutuları (TextBox, Button
/// vb. native kontrol yüzeyleri) BUNDAN ETKİLENMEZ - onlar her zaman nötr
/// beyaz/native kalır (bkz. MainWindow.xaml ResultCardBorderStyle,
/// QueryDropZone/ComparisonResultBorder Background="White").
/// </summary>
public readonly record struct ThemeColors(
    Color MainBackground,
    Color NeutralText,
    Color SectionHeader,
    Color SecondaryText,
    Color Success,
    Color Warning);

public static class ThemePalette
{
    /// <summary>
    /// Menüde gösterilecek sırayla (Açık, Normal, Koyu, Açık Sepya, Koyu
    /// Sepya, Lime) - MainWindow menü kurulumunda bu sıra esas alınır.
    /// </summary>
    public static readonly IReadOnlyList<AppTheme> MenuOrder = new[]
    {
        AppTheme.Acik,
        AppTheme.Normal,
        AppTheme.Koyu,
        AppTheme.AcikSepya,
        AppTheme.KoyuSepya,
        AppTheme.Lime,
    };

    public static string DisplayName(AppTheme theme) => theme switch
    {
        AppTheme.Acik => "Açık",
        AppTheme.Normal => "Normal",
        AppTheme.Koyu => "Koyu",
        AppTheme.AcikSepya => "Açık Sepya",
        AppTheme.KoyuSepya => "Koyu Sepya",
        AppTheme.Lime => "Lime (Deneme)",
        _ => theme.ToString(),
    };

    public static ThemeColors For(AppTheme theme) => theme switch
    {
        // Açık: orijinal (Faz 1-4) acik gri zemin - koyu metin.
        AppTheme.Acik => new ThemeColors(
            MainBackground: Color.FromRgb(0xF5, 0xF6, 0xF8),
            NeutralText: Color.FromRgb(0x22, 0x22, 0x22),
            SectionHeader: Color.FromRgb(0x33, 0x33, 0x33),
            SecondaryText: Color.FromRgb(0x66, 0x66, 0x66),
            Success: Color.FromRgb(0x2E, 0x7D, 0x32),
            Warning: Color.FromRgb(0xB0, 0x00, 0x20)),

        // Koyu: normal'den daha koyu lacivert-gri - acik metin.
        AppTheme.Koyu => new ThemeColors(
            MainBackground: Color.FromRgb(0x33, 0x41, 0x55),
            NeutralText: Colors.White,
            SectionHeader: Colors.White,
            SecondaryText: Color.FromRgb(0xC9, 0xD3, 0xDE),
            Success: Color.FromRgb(0x7C, 0xE4, 0x95),
            Warning: Color.FromRgb(0xFF, 0x8A, 0x80)),

        // Açık Sepya: sicak/bej acik zemin - koyu metin (Acik ile ayni rol).
        AppTheme.AcikSepya => new ThemeColors(
            MainBackground: Color.FromRgb(0xE8, 0xDC, 0xC8),
            NeutralText: Color.FromRgb(0x2A, 0x21, 0x18),
            SectionHeader: Color.FromRgb(0x2A, 0x21, 0x18),
            SecondaryText: Color.FromRgb(0x6B, 0x5D, 0x4D),
            Success: Color.FromRgb(0x2E, 0x7D, 0x32),
            Warning: Color.FromRgb(0xB0, 0x00, 0x20)),

        // Koyu Sepya: sicak/kahverengi koyu zemin - acik metin (Koyu ile ayni rol).
        AppTheme.KoyuSepya => new ThemeColors(
            MainBackground: Color.FromRgb(0x70, 0x5C, 0x46),
            NeutralText: Colors.White,
            SectionHeader: Colors.White,
            SecondaryText: Color.FromRgb(0xE4, 0xD9, 0xC7),
            Success: Color.FromRgb(0x7C, 0xE4, 0x95),
            Warning: Color.FromRgb(0xFF, 0x8A, 0x80)),

        // Lime (deneme): parlak sari-yesil zemin - koyu metin; basari rengi
        // ACIK yesil DEGIL, koyu/derin yesil - aksi halde zeminle karisir.
        AppTheme.Lime => new ThemeColors(
            MainBackground: Color.FromRgb(0xD4, 0xE1, 0x57),
            NeutralText: Color.FromRgb(0x1F, 0x2A, 0x1F),
            SectionHeader: Color.FromRgb(0x1F, 0x2A, 0x1F),
            SecondaryText: Color.FromRgb(0x3F, 0x4A, 0x3F),
            Success: Color.FromRgb(0x1B, 0x5E, 0x20),
            Warning: Color.FromRgb(0xB0, 0x00, 0x20)),

        // Normal: varsayilan - orta ton slate zemin, acik metin.
        _ => new ThemeColors(
            MainBackground: Color.FromRgb(0x64, 0x74, 0x8B),
            NeutralText: Colors.White,
            SectionHeader: Colors.White,
            SecondaryText: Color.FromRgb(0xDC, 0xE1, 0xE8),
            Success: Color.FromRgb(0x7C, 0xE4, 0x95),
            Warning: Color.FromRgb(0xFF, 0x8A, 0x80)),
    };
}
