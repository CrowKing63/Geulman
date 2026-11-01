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
            var bodyLines = new List<string>();
            foreach (var line in lines)
            {
                var idx = line.IndexOf("] ");
                if (idx > 0 && line.StartsWith('['))
                    bodyLines.Add(line.Substring(idx + 2));
                else
                    bodyLines.Add(line);
            }

            FileNameText.Text = Path.GetFileName(path);
            FormatText.Text = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            BodyEditor.Text = string.Join("\n", bodyLines);
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

            var find = FindBox.Text;
            var replace = ReplaceBox.Text;
            if (string.IsNullOrEmpty(find))
            {
                StatusText.Text = "Save: no find text";
                return;
            }

            var result = RunCli("edit", _currentFile, _currentFile, "--find", find, "--replace", replace);
            if (result != null)
            {
                ToastText.Text = "Saved in-place - verify by opening";
                ShowToast();
                StatusText.Text = "Saved: " + Path.GetFileName(_currentFile);
            }
            else
            {
                StatusText.Text = "Save failed (CLI error)";
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
