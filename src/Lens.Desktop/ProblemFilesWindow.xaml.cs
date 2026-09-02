using System.Windows;
using Lens.Core.Indexing;

namespace Lens.Desktop;

/// <summary>
/// [Faz 4C] Sade bir sorunlu-dosya listesi - MainWindow'u log viewer'a
/// donusturmemek icin ayri, tek-amacli bir pencere. Stack trace gostermez;
/// yalnizca IndexFileIssue.Reason'daki kisa mesaj gorunur.
/// </summary>
public partial class ProblemFilesWindow : Window
{
    public ProblemFilesWindow(IReadOnlyList<IndexFileIssue> issues)
    {
        InitializeComponent();
        IssuesGrid.ItemsSource = issues.Select(ToRow).ToList();
    }

    private static IssueRow ToRow(IndexFileIssue issue) => new(
        issue.FileName,
        issue.Extension,
        issue.Kind switch
        {
            FileIssueKind.UnsupportedImageFormat => "Desteklenmeyen görsel formatı",
            FileIssueKind.NonImageFile => "Desteklenmeyen dosya türü",
            _ => "Görsel okunamadı",
        },
        issue.Reason);

    private sealed record IssueRow(string FileName, string Extension, string KindText, string Reason);
}
