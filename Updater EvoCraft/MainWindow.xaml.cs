using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Linq;

namespace Updater_EvoCraft
{
    public partial class MainWindow : Window
    {
        private const string VersionCheckUrl = "https://www.dropbox.com/scl/fi/jasrtav5tgq8g6mkdzuv0/version.txt?rlkey=botlg6mgl3c4wpcazq61gnnor&st=6f42h66w&dl=1";
        private const string LauncherExeName = "EVO CRAFT LAUNCHER.exe";

        // --- Setări Installer ---
        private string installDirectory = @"C:\EvoCraftLauncherBeta";
        private const string DriversZipUrl = "https://evocraft.ro/download/Installer/drivers.zip";
        private const string LauncherZipUrl = "https://evocraft.ro/download/Installer/launcher/launcher.zip";
        private bool isInstallerMode = false;
        private Grid installerGrid;
        private TextBox txtInstallPath;

        public MainWindow()
        {
            InitializeComponent();
            this.MouseDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
            CheckModeAndSetupUI();
        }

        private void CheckModeAndSetupUI()
        {
            string[] args = Environment.GetCommandLineArgs();
            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string expectedLauncherPath = Path.Combine(rootDir, LauncherExeName);

            // Dacă Launcher-ul nu există și nu am primit semnal de la API (-verify/-update), pornim ca Installer
            if (!File.Exists(expectedLauncherPath) && !args.Contains("-verify") && !args.Contains("-update"))
            {
                isInstallerMode = true;
                CreateInstallerUI();
            }
        }

        private void CreateInstallerUI()
        {
            installerGrid = new Grid();
            installerGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2F33"));

            StackPanel panel = new StackPanel();
            panel.VerticalAlignment = VerticalAlignment.Center;
            panel.HorizontalAlignment = HorizontalAlignment.Center;

            TextBlock title = new TextBlock();
            title.Text = "EvoCraft Installer";
            title.Foreground = Brushes.White;
            title.FontSize = 24;
            title.FontWeight = FontWeights.Bold;
            title.Margin = new Thickness(0, 0, 0, 20);
            title.HorizontalAlignment = HorizontalAlignment.Center;

            txtInstallPath = new TextBox();
            txtInstallPath.Text = installDirectory;
            txtInstallPath.Width = 300;
            txtInstallPath.Height = 30;
            txtInstallPath.Margin = new Thickness(0, 0, 0, 10);
            txtInstallPath.VerticalContentAlignment = VerticalAlignment.Center;

            Button btnBrowse = new Button();
            btnBrowse.Content = "Selectează Folder";
            btnBrowse.Width = 150;
            btnBrowse.Height = 30;
            btnBrowse.Margin = new Thickness(0, 0, 0, 10);
            btnBrowse.Click += (s, e) => {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.SelectedPath = installDirectory;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        installDirectory = dialog.SelectedPath;
                        txtInstallPath.Text = installDirectory;
                    }
                }
            };

            Button btnInstall = new Button();
            btnInstall.Content = "Instalează EVO CRAFT";
            btnInstall.Width = 200;
            btnInstall.Height = 40;
            btnInstall.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#43b581"));
            btnInstall.Foreground = Brushes.White;
            btnInstall.FontWeight = FontWeights.Bold;
            btnInstall.Click += async (s, e) => {
                installerGrid.Visibility = Visibility.Collapsed;
                await ExecuteInstall();
            };

            panel.Children.Add(title);
            panel.Children.Add(txtInstallPath);
            panel.Children.Add(btnBrowse);
            panel.Children.Add(btnInstall);
            installerGrid.Children.Add(panel);

            // Suprapunem interfața de installer peste Grid-ul principal
            if (this.Content is Grid mainGrid)
            {
                mainGrid.Children.Add(installerGrid);
            }
            else
            {
                this.Content = installerGrid;
            }
        }

        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!isInstallerMode)
            {
                await HandleManualStart();
            }
        }

        private async Task ExecuteInstall()
        {
            try
            {
                installDirectory = txtInstallPath.Text.Trim();
                if (!Directory.Exists(installDirectory))
                {
                    Directory.CreateDirectory(installDirectory);
                }

                UpdateUI("Descărcare drivere necesare...", 15);
                await DownloadAndExtractFiles(DriversZipUrl, installDirectory);

                UpdateUI("Descărcare EvoCraft Launcher...", 45);
                await DownloadAndExtractFiles(LauncherZipUrl, installDirectory);

                UpdateUI("Configurare și creare copie Updater...", 85);
                string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
                string destUpdaterPath = Path.Combine(installDirectory, AppDomain.CurrentDomain.FriendlyName);

                // Creăm o copie a updater-ului/installer-ului în folderul ales
                if (!currentExePath.Equals(destUpdaterPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentExePath, destUpdaterPath, true);
                }

                UpdateUI("Instalare completă!", 100);
                await Task.Delay(1000);

                StartLauncherAndExit(Path.Combine(installDirectory, LauncherExeName));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la instalare: {ex.Message}");
                Application.Current.Shutdown();
            }
        }

        private async Task DownloadAndExtractFiles(string zipUrl, string targetDir)
        {
            string tempZip = Path.Combine(targetDir, "temp_package.zip");
            using (HttpClient client = new HttpClient())
            {
                byte[] response = await client.GetByteArrayAsync(zipUrl);
                await File.WriteAllBytesAsync(tempZip, response);
            }

            await Task.Run(async () =>
            {
                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string fullPath = Path.Combine(targetDir, entry.FullName);
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

            if (File.Exists(tempZip)) File.Delete(tempZip);
        }

        private string GetLocalLauncherVersion(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(filePath);
                    return $"{fvi.FileMajorPart}.{fvi.FileMinorPart}";
                }
            }
            catch { }
            return "0.0";
        }

        private async Task HandleManualStart()
        {
            try
            {
                UpdateUI("Verificare versiune...", 10);
                string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string launcherPath = Path.Combine(rootDir, LauncherExeName);

                string localVersion = GetLocalLauncherVersion(launcherPath);

                using (HttpClient client = new HttpClient())
                {
                    string data = await client.GetStringAsync(VersionCheckUrl);
                    string[] lines = data.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length >= 3)
                    {
                        string onlineVersion = lines[0].Trim();
                        string downloadUrl = lines[2].Trim();

                        if (onlineVersion == localVersion)
                        {
                            UpdateUI("Ești la ultima versiune!", 100);
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