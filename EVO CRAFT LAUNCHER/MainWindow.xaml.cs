using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Management;
using System.Windows.Media.Animation;
using System.Windows.Controls;
// Referinta catre libraria NuGet
using MCStatus;

namespace EVO_CRAFT_LAUNCHER
{
    public partial class MainWindow : Window
    {
        private const string CurrentLauncherVersion = "1.2";
        private string OnlineModpackVersion = "1.0";
        private const string ServerIP = "203.16.163.143";
        private const string ServerPort = "22036";
        private const string VersionCheckUrl = "https://www.dropbox.com/scl/fi/jasrtav5tgq8g6mkdzuv0/version.txt?rlkey=botlg6mgl3c4wpcazq61gnnor&st=6f42h66w&dl=1";
        private string CurrentModsDownloadUrl = "https://www.dropbox.com/scl/fi/i8l4bo3rkr374qwhturai/Evo-Craft.zip?rlkey=yzbu2h8sfm1rm9faz02a8t0z1&st=0wuvz5hl&dl=1";

        private MSession? _session;
        private MSession? _premiumSession = null;
        private bool _isPremiumMode = false;
        private bool _isRefreshing = false;
        private string _userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".evocraft");
        private double totalSystemRam = 8;

        public MainWindow()
        {
            InitializeComponent();
            txtGamePath.Text = _userDataPath;
            lblLauncherVersion.Text = $"v{CurrentLauncherVersion}";
            GetSystemRam();
            InitLauncherSequence();
        }

        private void GetSystemRam()
        {
            try
            {
                double totalCapacity = 0;
                ObjectQuery query = new ObjectQuery("SELECT Capacity FROM Win32_PhysicalMemory");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get())
                    totalCapacity += Convert.ToDouble(obj["Capacity"]);
                totalSystemRam = Math.Round(totalCapacity / 1024 / 1024 / 1024, 0);
                lblSystemRam.Text = $"SISTEMUL TĂU ARE {totalSystemRam} GB RAM TOTAL";
                sliderRam.Maximum = (int)totalSystemRam;
            }
            catch { lblSystemRam.Text = "DETECTARE RAM INDISPONIBILĂ"; }
        }

        private async void InitLauncherSequence()
        {
            if (!Directory.Exists(_userDataPath)) Directory.CreateDirectory(_userDataPath);
            string savedUser = Path.Combine(_userDataPath, "last_user.txt");
            if (File.Exists(savedUser)) txtUsername.Text = File.ReadAllText(savedUser);

            await CheckForUpdates();
            await RefreshServerStatus();
            UpdateLaunchButtonState();
            await Task.Delay(2500);
            SplashScreen.Visibility = Visibility.Collapsed;

            string loginModeFile = Path.Combine(_userDataPath, "login_mode.txt");
            if (File.Exists(loginModeFile) && File.ReadAllText(loginModeFile) == "premium")
            {
                if (File.Exists(Path.Combine(_userDataPath, "login_cache"))) await PerformMicrosoftLogin(true);
            }
            else
            {
                UpdateCrackedUI();
                await ShowProfile(string.IsNullOrWhiteSpace(txtUsername.Text) ? "MHF_Steve" : txtUsername.Text);
            }
        }

        private void UpdateLaunchButtonState()
        {
            string localVerFile = Path.Combine(_userDataPath, "modpack_version.info");
            string localVer = File.Exists(localVerFile) ? File.ReadAllText(localVerFile).Trim() : "";
            bool modsExist = Directory.Exists(Path.Combine(_userDataPath, "mods"));

            Dispatcher.Invoke(() => {
                if (!modsExist)
                {
                    btnLaunch.Content = "DESCARCĂ MODPACK";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(250, 166, 26));
                }
                else if (localVer != OnlineModpackVersion)
                {
                    btnLaunch.Content = "UPDATE MODPACK";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(114, 137, 218));
                }
                else
                {
                    btnLaunch.Content = "JOACĂ";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
                }
            });
        }

        // --- CORE STATUS (MCSTATUS.NET) ---
        private async Task RefreshServerStatus()
        {
            try
            {
                // GetStatusAsync returns StatusResponse, which does not have ServerOnline.
                // Instead, check if Description is not null/empty and Players.Max > 0 as a proxy for online status.
                var status = await ServerListClient.GetStatusAsync($"{ServerIP}:{ServerPort}");

                Dispatcher.Invoke(() => {
                    // Consider the server online if Description is not empty and Players.Max > 0
                    if (!string.IsNullOrWhiteSpace(status.Description) && status.Players.Max > 0)
                    {
                        string motd = status.Description;
                        int online = status.Players.Online;
                        int max = status.Players.Max;

                        lblServerInfo.Text = $"{motd} | {online}/{max}".ToUpper();
                        elServerStatus.Fill = new SolidColorBrush(Color.FromRgb(67, 181, 129));
                    }
                    else
                    {
                        lblServerInfo.Text = "SERVER OFFLINE";
                        elServerStatus.Fill = Brushes.Red;
                    }
                });
            }
            catch
            {
                Dispatcher.Invoke(() => {
                    lblServerInfo.Text = "OFFLINE";
                    elServerStatus.Fill = Brushes.Red;
                });
            }
        }

        // Aceasta este metoda care lipsea si cauza eroarea CS1061 in imaginea 1
        private async void btnRefreshServer_Click(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            lblServerInfo.Text = "VERIFICARE...";
            await RefreshServerStatus();
            _isRefreshing = false;
        }

        private void sliderRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblRamValue != null)
            {
                int val = (int)e.NewValue; lblRamValue.Text = $"{val} GB";
                if (val > (totalSystemRam * 0.85))
                {
                    lblRamValue.Foreground = Brushes.Red;
                    Storyboard? sb = this.FindResource("RamWarningAnim") as Storyboard; sb?.Begin();
                }
                else lblRamValue.Foreground = new SolidColorBrush(Color.FromRgb(67, 181, 129));
            }
        }

        private async void btnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPremiumMode)
            {
                if (string.IsNullOrWhiteSpace(txtUsername.Text)) return;
                _session = MSession.CreateOfflineSession(txtUsername.Text);
                File.WriteAllText(Path.Combine(_userDataPath, "last_user.txt"), txtUsername.Text);
            }
            else _session = _premiumSession;

            if (_session == null) return;
            btnLaunch.IsEnabled = false;

            try
            {
                await HandleModpackSync();
                var launcher = new MinecraftLauncher(new MinecraftPath(_userDataPath));
                lblStatus.Text = "Se pregătește Minecraft...";

                await launcher.InstallAsync("1.20.1", new Progress<InstallerProgressChangedEventArgs>(ev => {
                    Dispatcher.Invoke(() => {
                        pbProgress.Value = (double)ev.ProgressedTasks / ev.TotalTasks * 100;
                        lblProgressDetails.Text = $"{ev.Name} ({ev.ProgressedTasks}/{ev.TotalTasks})";
                    });
                }), new Progress<ByteProgress>());

                var forge = new ForgeInstaller(launcher);
                var forgeID = await forge.Install("1.20.1", "47.3.0");

                var process = await launcher.CreateProcessAsync(forgeID, new MLaunchOption
                {
                    MaximumRamMb = (int)sliderRam.Value * 1024,
                    Session = _session
                });

                if (chkDebugConsole.IsChecked == true) { process.StartInfo.UseShellExecute = false; process.StartInfo.CreateNoWindow = false; }
                this.Hide(); process.Start(); await Task.Run(() => process.WaitForExit()); this.Show();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { btnLaunch.IsEnabled = true; lblStatus.Text = "Gata de pornire"; pbProgress.Value = 0; UpdateLaunchButtonState(); }
        }

        private async Task HandleModpackSync()
        {
            string localVerFile = Path.Combine(_userDataPath, "modpack_version.info");
            string localVer = File.Exists(localVerFile) ? File.ReadAllText(localVerFile).Trim() : "";

            if (!Directory.Exists(Path.Combine(_userDataPath, "mods")) || localVer != OnlineModpackVersion)
            {
                lblStatus.Text = "DESCARCARE MODPACK...";
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(CurrentModsDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var zipPath = Path.Combine(_userDataPath, "mods.zip");

                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192]; long totalRead = 0; int read;
                        Stopwatch sw = Stopwatch.StartNew();
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read); totalRead += read;
                            if (totalBytes != -1)
                            {
                                double percentage = (double)totalRead / totalBytes * 100;
                                double speed = (totalRead / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds;
                                Dispatcher.Invoke(() => {
                                    pbProgress.Value = percentage;
                                    lblProgressDetails.Text = $"{Math.Round(speed, 2)} MB/s | {totalRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB";
                                });
                            }
                        }
                    }
                    lblStatus.Text = "EXTRAGERE...";
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, _userDataPath, true));
                    File.Delete(zipPath); File.WriteAllText(localVerFile, OnlineModpackVersion);
                }
            }
        }

        private async Task PerformMicrosoftLogin(bool silent = false)
        {
            try
            {
                var handler = new JELoginHandlerBuilder().WithAccountManager(Path.Combine(_userDataPath, "login_cache")).Build();
                _premiumSession = silent ? await handler.AuthenticateSilently() : await handler.AuthenticateInteractively();
                if (_premiumSession != null) { _isPremiumMode = true; UpdatePremiumUI(); await ShowProfile(_premiumSession.Username); }
            }
            catch { UpdateCrackedUI(); }
        }

        private void UpdateCrackedUI()
        {
            _isPremiumMode = false; btnTabCracked.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
            btnTabPremium.Background = Brushes.Transparent; pnlUsernameInput.Visibility = Visibility.Visible;
            pnlPremiumMsg.Visibility = Visibility.Collapsed; lblAccountType.Text = "Cont Cracked";
        }

        private void UpdatePremiumUI()
        {
            _isPremiumMode = true; btnTabPremium.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
            btnTabCracked.Background = Brushes.Transparent; pnlUsernameInput.Visibility = Visibility.Collapsed;
            pnlPremiumMsg.Visibility = Visibility.Visible; lblAccountType.Text = "Cont Premium";
        }

        private void btnTabCracked_Click(object sender, RoutedEventArgs e) => UpdateCrackedUI();
        private async void btnTabPremium_Click(object sender, RoutedEventArgs e) => await PerformMicrosoftLogin();

        private async void txtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPremiumMode) await ShowProfile(txtUsername.Text);
        }

        // Am adaugat '?' pentru a accepta null si a scapa de eroarea CS8604 din imaginea 1
        private async Task ShowProfile(string? u)
        {
            string user = string.IsNullOrEmpty(u) ? "MHF_Steve" : u;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync($"https://minotar.net/helm/{user}/100.png");
                    var img = new BitmapImage();
                    using (var ms = new MemoryStream(data)) { img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); }
                    imgSkin.ImageSource = img; lblProfileName.Text = user;
                }
            }
            catch { }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string data = await client.GetStringAsync(VersionCheckUrl);
                    string[] lines = data.Split('\n');
                    if (lines.Length >= 4)
                    {
                        OnlineModpackVersion = lines[1].Trim();
                        CurrentModsDownloadUrl = lines[3].Trim();
                        Dispatcher.Invoke(() => lblModpackVersion.Text = $"MODPACK: v{OnlineModpackVersion}");
                    }
                }
            }
            catch { }
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Vrei să te deconectezi?", "Log Out", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _premiumSession = null; UpdateCrackedUI(); _ = ShowProfile("MHF_Steve");
            }
        }

        private void btnDeleteData_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Ștergi modpack-ul complet?", "Resetare", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (Directory.Exists(_userDataPath))
                    {
                        Directory.Delete(_userDataPath, true);
                        Directory.CreateDirectory(_userDataPath);
                        UpdateLaunchButtonState();
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void btnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void btnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void btnSettings_Click(object sender, RoutedEventArgs e) => pnlSettings.Visibility = Visibility.Visible;
        private void btnCloseSettings_Click(object sender, RoutedEventArgs e) => pnlSettings.Visibility = Visibility.Collapsed;
        private void bgVideo_Loaded(object sender, RoutedEventArgs e) => bgVideo.Play();
        private void bgVideo_MediaEnded(object sender, RoutedEventArgs e) { bgVideo.Position = TimeSpan.FromMilliseconds(1); bgVideo.Play(); }
        private void btnBrowsePath_Click(object sender, RoutedEventArgs e) { }
        private void OpenTikTok(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://tiktok.com/@evocraftro") { UseShellExecute = true });
        private void OpenYouTube(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://youtube.com/@EvoCraftRo") { UseShellExecute = true });
        private void OpenDiscord(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://discord.gg/e9yHMaMwTQ") { UseShellExecute = true });
    }
}