using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Lens.Desktop;

/// <summary>
/// [Faz 4D polish] Sade buyuk-onizleme/zoom penceresi. MainWindow'un layout'unu
/// etkilemez - ayri, tek-amacli bir pencere (bkz. ProblemFilesWindow deseni).
/// Goruntu cagiran tarafindan (MainWindow.TryOpenImagePreview) zaten diskten
/// basariyla okunmus ve dondurulmus (Freeze) olarak verilir; bu pencere
/// icinde hicbir dosya G/C islemi yapilmaz - ag/dosya hatasi riski burada yok.
/// </summary>
public partial class ImagePreviewWindow : Window
{
    private const double MinZoom = 0.5;
    private const double MaxZoom = 5.0;
    private const double WheelZoomFactor = 1.15;

    private bool _isPanning;
    private Point _panStartMouse;
    private Point _panStartOffset;

    public ImagePreviewWindow(BitmapImage image, string title)
    {
        InitializeComponent();
        PreviewImage.Source = image;
        TitleText.Text = title;
        Title = $"Önizleme — {title}";
        SizeToImage(image);
    }

    /// <summary>
    /// [Faz 4D polish - kullanici geri bildirimi] Pencere, gorselin TAM
    /// COZUNURLUGUNE gore degil, MainWindow'daki kucuk onizleme kutusuna
    /// (220x220) gore "biraz daha buyuk" acilir - yuksek cozunurluklu bir
    /// telefon fotografi bile ekrani kaplamasin. Detay incelemek isteyen
    /// kullanici zoom (tekerlek) veya pencereyi kendisi buyuterek/maximize
    /// ederek (varsayilan ResizeMode=CanResize) devam eder. Uzun kenar sabit
    /// tutulur, kisa kenar gorselin en-boy oranina gore hesaplanir - dikey/
    /// yatay gorseller bozulmadan olceklenir.
    /// </summary>
    private void SizeToImage(BitmapImage image)
    {
        const double baseLongEdge = 480; // MainWindow'daki 220px kutudan "biraz daha buyuk"
        const double margin = 40;
        const double minWidth = 360;
        const double minHeight = 280;

        var aspectRatio = (double)image.PixelWidth / image.PixelHeight;
        double contentWidth, contentHeight;
        if (aspectRatio >= 1)
        {
            contentWidth = baseLongEdge;
            contentHeight = baseLongEdge / aspectRatio;
        }
        else
        {
            contentHeight = baseLongEdge;
            contentWidth = baseLongEdge * aspectRatio;
        }

        var workArea = SystemParameters.WorkArea;
        var maxWidth = workArea.Width * 0.85;
        var maxHeight = workArea.Height * 0.85;

        var desiredWidth = Math.Min(contentWidth + margin, maxWidth);
        var desiredHeight = Math.Min(contentHeight + margin, maxHeight);

        Width = Math.Max(desiredWidth, minWidth);
        Height = Math.Max(desiredHeight, minHeight);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void ImageHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? WheelZoomFactor : 1 / WheelZoomFactor;
        var newScale = Math.Clamp(ZoomTransform.ScaleX * factor, MinZoom, MaxZoom);
        ZoomTransform.ScaleX = newScale;
        ZoomTransform.ScaleY = newScale;
        e.Handled = true;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetView();
            return;
        }

        _isPanning = true;
        _panStartMouse = e.GetPosition(this);
        _panStartOffset = new Point(PanTransform.X, PanTransform.Y);
        ImageHost.CaptureMouse();
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var current = e.GetPosition(this);
        PanTransform.X = _panStartOffset.X + (current.X - _panStartMouse.X);
        PanTransform.Y = _panStartOffset.Y + (current.Y - _panStartMouse.Y);
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan();

    private void ImageHost_MouseLeave(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ImageHost.ReleaseMouseCapture();
    }

    /// <summary>Cift tik: goruntuyu ilk "sigdir" durumuna (zoom=1, pan=0) sifirlar.</summary>
    private void ResetView()
    {
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }
}
