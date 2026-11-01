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

            // 4a: Load recent files
            LoadRecentFiles();
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

        // ── 4a: Recent files ─────────────────────────────────────────────
        private void LoadRecentFiles()
        {
            var recent = RecentFiles.Load();
            if (recent.Count > 0)
            {
                RecentHeader.Visibility = Visibility.Visible;
                RecentList.Visibility = Visibility.Visible;
                RecentList.ItemsSource = recent;
            }
        }

        private void RecentList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (RecentList.SelectedItem is string path && File.Exists(path))
                OpenFile(path);
        }

        // ── 4c: Encrypted document detection ──────────────────────────────
        private bool IsEncryptedDocx(string path)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                // Encrypted docx has no [Content_Types].xml, only encrypted parts
                foreach (var entry in zip.Entries)
                    if (entry.FullName == "[Content_Types].xml") return false;
                return true;
            }
            catch { return false; }
        }

        private void OpenFile(string path)
        {
            // 4c: Reject encrypted documents
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx" && IsEncryptedDocx(path))
            {
                StatusText.Text = "암호화된 문서는 지원되지 않습니다";
                return;
            }

            _currentFile = path;
            RecentFiles.Save(path); // 4a: update recent list

            var result = RunCli("open", path);
            if (result == null)
            {
                StatusText.Text = "문서 열기 실패";
                return;
            }

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
            FormatText.Text = ext.TrimStart('.').ToUpperInvariant();
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

            var raw = BodyEditor.Text.Replace("\r\n", "\n").Replace("\r", "\n");
            var currentParas = raw.Split('\n');
            if (currentParas.Length > 0 && currentParas[^1] == "")
                currentParas = currentParas[..^1];

            if (currentParas.Length != _originalParas.Count)
            {
                StatusText.Text = $"문단 수 불일치 ({currentParas.Length} vs {_originalParas.Count}) — v1에서 지원되지 않습니다";
                return;
            }

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

        private string? RunCli(string command, params string[] args)
        {
            var baseDir = System.AppContext.BaseDirectory;
            var cliPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Geulman.Docx", "bin", "Debug", "net8.0", "Geulman.Docx.dll"));
            var dotnetPath = Path.Combine(@"C:\Program Files\dotnet", "dotnet.exe");

            var tmpOut = Path.GetTempFileName();
            var tmpErr = Path.GetTempFileName();

            var argStr = string.Join(" ", args.Select(a => $"\"{a}\""));
            var cmd = $"/c \"\"{dotnetPath}\" \"{cliPath}\" {command} {argStr} > \"\"{tmpOut}\"\" 2> \"\"{tmpErr}\"\"\"";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    Arguments = cmd,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;
                proc.WaitForExit(60000);

                // Diagnostic: log everything
                var dbg = Path.Combine(Path.GetTempPath(), "geulman-cli-diag.txt");
                File.WriteAllText(dbg,
                    $"command: {command}\n" +
                    $"cliPath: {cliPath}\n" +
                    $"exitCode: {proc.ExitCode}\n" +
                    $"--- stdout ---\n" +
                    (File.Exists(tmpOut) ? File.ReadAllText(tmpOut) : "(missing)") +
                    $"--- stderr ---\n" +
                    (File.Exists(tmpErr) ? File.ReadAllText(tmpErr) : "(missing)"));

                if (proc.ExitCode != 0)
                {
                    StatusText.Text = $"CLI error (exit {proc.ExitCode}) → {dbg}";
                    return null;
                }

                var output = File.ReadAllText(tmpOut);
                return output.TrimEnd('\r', '\n');
            }
            catch (Exception ex)
            {
                StatusText.Text = $"RunCli exception: {ex.Message}";
                return null;
            }
            finally
            {
                try { File.Delete(tmpOut); } catch { }
                try { File.Delete(tmpErr); } catch { }
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

    }
}
