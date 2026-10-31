using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;
using System;

namespace Geulman;

public partial class MainWindow : Window
{
    string? _currentFile;
    string _format = "";
    int _replaceCount = 0;

    public MainWindow()
    {
        this.InitializeComponent();
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(780, 640));
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = OpenFileAsync();
    }

    private async System.Threading.Tasks.Task OpenFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".hwp");
        picker.FileTypeFilter.Add(".hwpx");
        picker.FileTypeFilter.Add(".docx");
        var file = await picker.PickSingleFileAsync().AsTask();
        if (file != null)
            OpenFile(file.Path);
    }

    private void OpenFile(string path)
    {
        _currentFile = path;
        _format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

        var text = RunCli("open", path);
        if (text == null) return;

        EmptyState.Visibility = Visibility.Collapsed;
        EditState.Visibility = Visibility.Visible;

        FileNameText.Text = Path.GetFileName(path);
        FormatText.Text = _format;
        BodyEditor.Text = text;
        UpdateCharCount();
        StatusText.Text = "";
    }

    private void UpdateCharCount()
    {
        CharCountText.Text = BodyEditor.Text.Length + "자";
    }

    private void FindReplaceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FindPanel.Visibility == Visibility.Visible)
            FindPanel.Visibility = Visibility.Collapsed;
        else
            FindPanel.Visibility = Visibility.Visible;
    }

    private void CloseFindBtn_Click(object sender, RoutedEventArgs e)
    {
        FindPanel.Visibility = Visibility.Collapsed;
        FindBox.Text = "";
        ReplaceBox.Text = "";
        _replaceCount = 0;
        StatusText.Text = "";
    }

    private void ReplaceAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var find = FindBox.Text;
        var replace = ReplaceBox.Text;
        if (string.IsNullOrEmpty(find)) return;

        _replaceCount = 0;
        var text = BodyEditor.Text;
        while (true)
        {
            int idx = text.IndexOf(find, StringComparison.Ordinal);
            if (idx < 0) break;
            text = text.Remove(idx, find.Length).Insert(idx, replace);
            _replaceCount++;
        }
        BodyEditor.Text = text;
        UpdateCharCount();
        StatusText.Text = _replaceCount + "개 교체됨";
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) return;

        var find = FindBox.Text;
        var replace = ReplaceBox.Text;
        if (string.IsNullOrEmpty(find))
        {
            StatusText.Text = "저장: 변경 사항 없음";
            return;
        }

        var outPath = _currentFile + ".geulman-out";
        var result = RunCli("edit", _currentFile, outPath, "--find", find, "--replace", replace);
        if (result != null)
        {
            ToastText.Text = "원본 형식으로 저장했습니다 — 서식·코멘트·변경 사항 보존됨.";
            ShowToast();
            StatusText.Text = "저장 완료: " + Path.GetFileName(outPath);
        }
        else
        {
            StatusText.Text = "저장 실패 (CLI 오류)";
        }
    }

    private void ShowToast()
    {
        Toast.Visibility = Visibility.Visible;
        _ = ToastAutoHideAsync();
    }

    private async System.Threading.Tasks.Task ToastAutoHideAsync()
    {
        await System.Threading.Tasks.Task.Delay(4000);
        Toast.Visibility = Visibility.Collapsed;
    }

    private string? RunCli(string cmd, params string[] args)
    {
        var exePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, "..", "..", "Geulman.Docx", "bin", "Debug", "net8.0", "Geulman.Docx.exe"));
        if (!File.Exists(exePath)) return null;

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
        catch { return null; }
    }
}