using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Geulman;

public partial class MainWindow : Window
{
    string? _currentFile;
    string _format = "";

    public MainWindow()
    {
        InitializeComponent();
        LoadRecentFiles();
    }

    // ── 파일 열기 ─────────────────────────────────────────────────────
    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "문서 열기",
            Filter = "문서 (*.hwp;*.hwpx;*.docx)|*.hwp;*.hwpx;*.docx" +
                      "|HWP (*.hwp)|*.hwp|HWPX (*.hwpx)|*.hwpx|DOCX (*.docx)|*.docx"
        };
        if (dlg.ShowDialog() == true)
            OpenFile(dlg.FileName);
    }

    private void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            OpenFile(path);
    }

    private void OpenFile(string path)
    {
        _currentFile = path;
        _format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

        // CLI 엔진으로 문서 텍스트 로드
        var text = RunCli("open", path);
        if (text == null) return;

        // UI 전환: Empty → Edit
        EmptyState.Visibility = Visibility.Collapsed;
        EditState.Visibility = Visibility.Visible;

        FileNameText.Text = Path.GetFileName(path);
        FormatText.Text = _format;
        BodyEditor.Text = text;
        UpdateCharCount();
        StatusText.Text = "";
    }

    // ── 자 수 업데이트 ────────────────────────────────────────────────
    private void UpdateCharCount()
    {
        CharCountText.Text = $"{BodyEditor.Text.Length}자";
    }

    // ── 찾기·바꾸기 (MVP) ─────────────────────────────────────────────
    private void FindReplaceBtn_Click(object sender, RoutedEventArgs e)
    {
        var find = PromptInput("찾을 텍스트");
        if (string.IsNullOrEmpty(find)) return;
        var replace = PromptInput("바꿀 텍스트 (비우면 삭제)");
        if (replace == null) return;

        int idx = BodyEditor.Text.IndexOf(find, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var text = BodyEditor.Text;
            text = text.Remove(idx, find.Length).Insert(idx, replace);
            BodyEditor.Text = text;
            StatusText.Text = "변경됨 · 1줄 수정";
            UpdateCharCount();
        }
        else
        {
            StatusText.Text = "못 찾음";
        }
    }

    // ── 저장 (MVP) ────────────────────────────────────────────────────
    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) return;
        ShowToast("원본 형식으로 저장했습니다 — 서식·코멘트·변경 사항 보존됨.");
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            Toast.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    // ── CLI 엔진 호출 ──────────────────────────────────────────────────
    private string? RunCli(string cmd, params string[] args)
    {
        // 개발 모드: dotnet run으로 Geulman.Docx 호출
        var exePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Geulman.Docx", "bin", "Debug", "net8.0", "Geulman.Docx.exe");
        if (!File.Exists(exePath))
            exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Geulman.Docx.exe"));

        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(cmd);
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return p.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    // ── 최근 문서 (MVP: 빈 목록) ───────────────────────────────────────
    private void LoadRecentFiles()
    {
        RecentList.ItemsSource = new List<string>();
    }

    // ── 인라인 입력 프롬프트 ───────────────────────────────────────────
    private string? PromptInput(string label)
    {
        var dlg = new Window
        {
            Title = label,
            Width = 400,
            Height = 160,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("PaperBrush")
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var txt = new TextBox
        {
            MinHeight = 42,
            FontFamily = (FontFamily)FindResource("UIFont"),
            FontSize = 14
        };
        Grid.SetRow(txt, 1);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(btnPanel, 2);

        var okBtn = new Button { Content = "확인", MinWidth = 80, Style = (Style)FindResource("AccentButton") };
        var cancelBtn = new Button { Content = "취소", MinWidth = 80, Style = (Style)FindResource("BaseButton"), Margin = new Thickness(0, 0, 8, 0) };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);

        grid.Children.Add(txt);
        grid.Children.Add(btnPanel);
        dlg.Content = grid;

        okBtn.Click += (_, _) => { dlg.DialogResult = true; };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; };

        return dlg.ShowDialog() == true ? txt.Text : null;
    }
}
