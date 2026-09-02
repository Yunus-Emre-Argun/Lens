using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lens.Desktop;

/// <summary>[Faz 4D polish] Native MessageBox.Show yerine kullanilan uyari turleri.</summary>
public enum AlertKind
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// [Faz 4D polish - kullanici geri bildirimi] Native Win32 MessageBox'in
/// kucuk/okunmasi zor gorunumu yerine, uygulamanin geri kalaniyla tutarli,
/// sade ve rahat okunur bir uyari penceresi. Yeni bir dialog framework
/// degil - ProblemFilesWindow/ImagePreviewWindow ile ayni desende, tek
/// amacli kucuk bir native WPF Window.
/// </summary>
public partial class AlertWindow : Window
{
    private static readonly Brush AccentIconBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    private static readonly Brush WarningIconBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20));

    private AlertWindow(string message, string title, AlertKind kind)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        (IconText.Text, IconText.Foreground) = kind switch
        {
            AlertKind.Warning => ("⚠", WarningIconBrush),
            AlertKind.Error => ("⛔", WarningIconBrush),
            _ => ("ℹ", AccentIconBrush),
        };
    }

    /// <summary>Mevcut MessageBox.Show(owner, message, title, ..., image) cagrilarinin dogrudan yerini alir - modal, OK-only.</summary>
    public static void Show(Window owner, string message, string title, AlertKind kind)
    {
        var alert = new AlertWindow(message, title, kind) { Owner = owner };
        alert.ShowDialog();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
