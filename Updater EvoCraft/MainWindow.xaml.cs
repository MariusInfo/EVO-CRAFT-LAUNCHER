using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Updater_EvoCraft
{
    public partial class MainWindow : Window
    {
        private const string VersionCheckUrl = "https://www.dropbox.com/scl/fi/jasrtav5tgq8g6mkdzuv0/version.txt?rlkey=botlg6mgl3c4wpcazq61gnnor&st=6f42h66w&dl=1";
        private const string LauncherExeName = "EVO CRAFT LAUNCHER.exe";

        public MainWindow()
        {
            InitializeComponent();
            this.MouseDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        }

        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            await HandleManualStart();
        }

        private string GetLocalLauncherVersion(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    // Citește versiunea din proprietățile fișierului (File -> Properties -> Details -> File Version)
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(filePath);
                    return $"{fvi.FileMajorPart}.{fvi.FileMinorPart}";
                }
            }
            catch { }
            return "0.0"; // Versiune default dacă fișierul nu există sau nu are versiune
        }

        private async Task HandleManualStart()
        {
            try
            {
                UpdateUI("Verificare versiune...", 10);
                string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string launcherPath = Path.Combine(rootDir, LauncherExeName);

                // --- DETECTARE AUTOMATĂ VERSIUNE LOCALĂ ---
                string localVersion = GetLocalLauncherVersion(launcherPath);

                using (HttpClient client = new HttpClient())
                {
                    string data = await client.GetStringAsync(VersionCheckUrl);
                    string[] lines = data.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length >= 3)
                    {
                        string onlineVersion = lines[0].Trim();
                        string downloadUrl = lines[2].Trim();

                        // Comparăm versiunea detectată local cu cea de pe server
                        if (onlineVersion == localVersion)
                        {
                            UpdateUI("Ești la zi!", 100);
                            await Task.Delay(1000);
                            StartLauncherAndExit(launcherPath);
                        }
                        else
                        {
                            UpdateUI($"Update disponibil: {localVersion} -> {onlineVersion}", 15);
                            await Task.Delay(500);
                            await ExecuteUpdate(downloadUrl, rootDir, LauncherExeName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la pornire: {ex.Message}");
                Application.Current.Shutdown();
            }
        }

        private async Task ExecuteUpdate(string zipUrl, string rootDir, string launcherExe)
        {
            string tempZip = Path.Combine(rootDir, "update_package.zip");
            string launcherPath = Path.Combine(rootDir, launcherExe);

            try
            {
                await KillLauncherProcesses(launcherExe);

                UpdateUI("Descărcare update...", 30);
                using (HttpClient client = new HttpClient())
                {
                    byte[] response = await client.GetByteArrayAsync(zipUrl);
                    await File.WriteAllBytesAsync(tempZip, response);
                }

                UpdateUI("Instalare fișiere...", 70);
                await Task.Run(async () =>
                {
                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string fullPath = Path.Combine(rootDir, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(fullPath);
                                continue;
                            }

                            string? parentDir = Path.GetDirectoryName(fullPath);
                            if (parentDir != null) Directory.CreateDirectory(parentDir);

                            bool success = false;
                            for (int retry = 0; retry < 5; retry++)
                            {
                                try
                                {
                                    entry.ExtractToFile(fullPath, true);
                                    success = true;
                                    break;
                                }
                                catch (IOException) { await Task.Delay(1000); }
                            }
                            if (!success) throw new Exception($"Eroare la fișierul: {entry.Name}");
                        }
                    }
                });

                UpdateUI("Actualizat cu succes!", 100);
                if (File.Exists(tempZip)) File.Delete(tempZip);

                await Task.Delay(1000);
                StartLauncherAndExit(launcherPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"EROARE LA UPDATE:\n{ex.Message}");
                Application.Current.Shutdown();
            }
        }

        private async Task KillLauncherProcesses(string launcherExe)
        {
            string processName = Path.GetFileNameWithoutExtension(launcherExe);
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { p.Kill(); await Task.Run(() => p.WaitForExit(3000)); } catch { }
            }
        }

        private void StartLauncherAndExit(string path)
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            Application.Current.Shutdown();
        }

        private void UpdateUI(string status, double progress)
        {
            Dispatcher.Invoke(() => {
                lblStatus.Text = status;
                pbUpdate.Value = progress;
                lblPercentage.Text = $"{(int)progress}%";
                if (progress >= 100) pbUpdate.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#43b581"));
            });
        }
    }
}