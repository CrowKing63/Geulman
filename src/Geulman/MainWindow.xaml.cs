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
        StatusText.Text = "찾기·바꾸기 (MVP: 향후 ContentDialog)";
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) return;
        ShowToast();
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
