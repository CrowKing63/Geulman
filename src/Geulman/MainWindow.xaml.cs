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

        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(780, 640));
            this.Activate();
            this.Title = "Geulman";
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = NativeMethods.ShowOpenFileDialog();
                if (!string.IsNullOrEmpty(path))
                    OpenFile(path);
            }
            catch
            {
                // File dialog can throw in unpackaged context; ignore.
            }
        }

        private void OpenFile(string path)
        {
            _currentFile = path;
            var result = RunCli("open", path);
            if (result == null) return;

            var lines = result.Split('\n');
            var bodyLines = new List<string>();
            int headerEnd = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("[header]"))
                {
                    headerEnd = i;
                    continue;
                }
                if (headerEnd >= 0)
                    bodyLines.Add(lines[i]);
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
            var cliPath = Path.Combine(baseDir, "..", "..", "..", "Geulman.Docx", "bin", "Debug", "net8.0", "Geulman.Docx.dll");

            var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                Arguments = "\"" + cliPath + "\" " + command + " " + string.Join(" ", args.Select(a => "\"" + a + "\"")),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct OPENFILENAME
        {
            public uint lStructSize;
            public System.IntPtr owner;
            public System.IntPtr instance;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string filter;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public char[] file;
            public uint fileLength;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string title;
            public uint flags;
            public short fileType;
            public short multiSelect;
            public System.IntPtr extra;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string subDir;
            public short subDirLength;
        }

        [System.Runtime.InteropServices.DllImport("comdlg32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        public static string? ShowOpenFileDialog()
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(ofn);
            ofn.owner = IntPtr.Zero;
            ofn.instance = IntPtr.Zero;
            ofn.filter = "HWP\0*.hwp;*.hwpx\0DOCX\0*.docx\0All\0*.*\0\0";
            ofn.file = new char[260];
            ofn.fileLength = 260;
            ofn.title = "Open document";
            ofn.flags = 0x00080000 | 0x00000200;
            ofn.fileType = 0;
            ofn.multiSelect = 0;
            ofn.extra = IntPtr.Zero;
            ofn.subDir = "";
            ofn.subDirLength = 0;

            if (GetOpenFileName(ref ofn))
                return new string(ofn.file).TrimEnd('\0');
            return null;
        }
    }
}
