using System.Windows;

namespace Lens.Desktop;

/// <summary>
/// [Ayarlar sadeleştirme] Salt okunur, kullanıcı odaklı bir durum özeti - eski
/// AlertWindow tabanlı "Ayarlar" mesajının yerine geçer (bkz. MainWindow.
/// SettingsMenuItem_Click). Teknik dosya yolları (yönetici config/kullanıcı
/// ayarları/log/önbellek/model) normal kullanıcıdan varsayılan olarak KAPALI
/// bir "Teknik ayrıntılar" Expander'ında - hiçbir silme/temizleme/klasör açma
/// KOMUTU içermez, yalnızca GÖRÜNTÜLEME. Kapatma dışında hiçbir tercihi
/// değiştirmez/kaydetmez.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(
        string folderStatus,
        string adminConfigPath,
        string userSettingsPath,
        string logsDirectory,
        string cacheDirectory,
        string modelPath)
    {
        InitializeComponent();
        FolderStatusText.Text = folderStatus;
        AdminConfigPathText.Text = adminConfigPath;
        UserSettingsPathText.Text = userSettingsPath;
        LogsPathText.Text = logsDirectory;
        CachePathText.Text = cacheDirectory;
        ModelPathText.Text = modelPath;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
