using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Geulman
{
    public partial class MainWindow : Window
    {
        private string? _currentFile;
        private string _browseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        private string? _selectedFile;
        private List<string> _originalParas = new();

        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(780, 640));
            this.Activate();
            this.Title = "Geulman";
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            FileBrowser.Visibility = Visibility.Visible;
            _selectedFile = null;
            OpenBrowseBtn.IsEnabled = false;
            NavigateTo(_browseDir);
        }

        private void NavigateTo(string dir)
        {
            try
            {
                _browseDir = dir;
                PathText.Text = dir;
                var entries = new List<FileInfoEntry>();

                foreach (var d in Directory.GetDirectories(dir))
                    entries.Add(new FileInfoEntry { Name = "[ ] " + Path.GetFileName(d), IsDir = true, FullPath = d });

                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".hwp" || ext == ".hwpx" || ext == ".docx")
                        entries.Add(new FileInfoEntry { Name = Path.GetFileName(f), IsDir = false, FullPath = f });
                }

                FileList.ItemsSource = entries;
            }
            catch (Exception ex)
            {
                FileBrowserHint.Text = "Error: " + ex.Message;
            }
        }

        private void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(_browseDir);
            if (parent != null)
                NavigateTo(parent.FullName);
        }

        private void FileList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem is FileInfoEntry entry)
            {
                if (entry.IsDir)
                {
                    NavigateTo(entry.FullPath);
                    return;
                }
                _selectedFile = entry.FullPath;
                OpenBrowseBtn.IsEnabled = true;
                FileBrowserHint.Text = Path.GetFileName(entry.FullPath);
            }
        }

        private void CancelBrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            FileBrowser.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        private void OpenBrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFile == null) return;
            FileBrowser.Visibility = Visibility.Collapsed;
            OpenFile(_selectedFile);
        }

        private class FileInfoEntry
        {
            public string Name { get; set; } = "";
            public bool IsDir { get; set; }
            public string FullPath { get; set; } = "";
        }

        private void OpenFile(string path)
        {
            _currentFile = path;
            var result = RunCli("open", path);
            if (result == null) return;

            // CLI output: "[N] text" per line — strip the index prefix
            var lines = result.Split('\n');
            _originalParas.Clear();
            foreach (var line in lines)
            {
                var t = line.TrimEnd('\r');
                var idx = t.IndexOf("] ");
                if (idx > 0 && t.StartsWith('['))
                    _originalParas.Add(t.Substring(idx + 2));
                else
                    _originalParas.Add(t);
            }

            FileNameText.Text = Path.GetFileName(path);
            FormatText.Text = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            BodyEditor.Text = string.Join("\n", _originalParas);
            CharCountText.Text = BodyEditor.Text.Length + " chars";

            EmptyState.Visibility = Visibility.Collapsed;
            EditState.Visibility = Visibility.Visible;
            StatusText.Text = "";
        }

        private void FindReplaceBtn_Click(object sender, RoutedEventArgs e)
        {
            FindPanel.Visibility = Visibility.Visible;
            FindBox.Focus(FocusState.Programmatic);
        }

        private void CloseFindBtn_Click(object sender, RoutedEventArgs e)
        {
            FindPanel.Visibility = Visibility.Collapsed;
            FindBox.Text = "";
            ReplaceBox.Text = "";
        }

        private void ReplaceAllBtn_Click(object sender, RoutedEventArgs e)
        {
            var find = FindBox.Text;
            if (string.IsNullOrEmpty(find)) return;

            var text = BodyEditor.Text;
            var count = 0;
            var idx = 0;
            while ((idx = text.IndexOf(find, idx, System.StringComparison.Ordinal)) >= 0)
            {
                text = text.Remove(idx, find.Length).Insert(idx, ReplaceBox.Text);
                count++;
                idx += ReplaceBox.Text.Length;
            }
            BodyEditor.Text = text;
            CharCountText.Text = BodyEditor.Text.Length + " chars";
            StatusText.Text = count + " replacements";
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile == null) return;

            var currentParas = BodyEditor.Text.Split('\n');
            if (currentParas.Length != _originalParas.Count)
            {
                StatusText.Text = "문단 수 변경은 v1에서 지원되지 않습니다";
                return;
            }

            // Find changed paragraphs
            var edited = new List<(int Index, string Text)>();
            for (int i = 0; i < currentParas.Length; i++)
            {
                if (currentParas[i] != _originalParas[i])
                    edited.Add((i, currentParas[i]));
            }

            if (edited.Count == 0)
            {
                StatusText.Text = "변경된 문단이 없습니다";
                return;
            }

            // Build CLI args: save-edited <in> <out> --para "idx:text" ...
            var cliArgs = new List<string> { _currentFile, _currentFile };
            foreach (var (idx, text) in edited)
            {
                cliArgs.Add("--para");
                cliArgs.Add($"{idx}:{text}");
            }

            var result = RunCli("save-edited", cliArgs.ToArray());
            if (result != null)
            {
                ToastText.Text = "원본 형식으로 저장했습니다 — 서식·코멘트·변경 사항 보존됨";
                ShowToast();
                StatusText.Text = edited.Count + " 문단 수정됨";
            }
            else
            {
                StatusText.Text = "저장 실패 (CLI error)";
            }
        }

        private void ShowToast()
        {
            Toast.Visibility = Visibility.Visible;
            _ = HideToastAfterDelay();
        }

        private async System.Threading.Tasks.Task HideToastAfterDelay()
        {
            await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(4));
            Toast.Visibility = Visibility.Collapsed;
        }

        private string? RunCli(string command, params string[] args)
        {
            var baseDir = System.AppContext.BaseDirectory;
            var cliPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Geulman.Docx", "bin", "Debug", "net8.0", "geulman.dll"));
            var dotnetPath = Path.Combine(@"C:\Program Files\dotnet", "dotnet.exe");

            var psi = new System.Diagnostics.ProcessStartInfo(dotnetPath)
            {
                Arguments = "\"" + cliPath + "\" " + command + " " + string.Join(" ", args.Select(a => "\"" + a + "\"")),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8;

            try
            {
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(30000);
                return proc.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
