using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using DiscordRPC;
using MCStatus;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace EVO_CRAFT_LAUNCHER
{
    public class ModpackManifest
    {
        [JsonProperty("files")]
        public List<ModFileInfo> Files { get; set; } = new List<ModFileInfo>();
    }

    public class ModFileInfo
    {
        [JsonProperty("path")]
        public string Path { get; set; } = "";
        [JsonProperty("url")]
        public string Url { get; set; } = "";
        [JsonProperty("hash")]
        public string Hash { get; set; } = "";
    }

    public class PlayerStats
    {
        [JsonProperty("username")]
        public string Username { get; set; } = "";
        [JsonProperty("time")]
        public long TotalSeconds { get; set; }

        public string DisplayTime
        {
            get
            {
                TimeSpan t = TimeSpan.FromSeconds(TotalSeconds);
                return string.Format("{0}h {1}m", (int)t.TotalHours, t.Minutes);
            }
        }
    }

    public partial class MainWindow : Window
    {
        private const string CurrentLauncherVersion = "2.6";

        private string OnlineLauncherVersion = "";
        private string LauncherDownloadUrl = "";
        private string CurrentModsDownloadUrl = "";

        // =========================================================================
        // VARIABILE SISTEM SECTIUNI (4 MODURI)
        // =========================================================================
        private string _selectedGameMode = "Survival";

        private string OnlineModpackVersionSurvival = "1.0";
        private string ModsDownloadUrlSurvival = "";

        private string OnlineModpackVersionSkyblock = "1.0";
        private string ModsDownloadUrlSkyblock = "";

        private string OnlineModpackVersionCreative = "1.0";
        private string ModsDownloadUrlCreative = "";

        private string OnlineModpackVersionParkour = "1.0";
        private string ModsDownloadUrlParkour = "";

        private string ServerIP = "45.13.151.19";
        private string ServerPort = "25565";

        private static readonly string VpsApiUrl = "http://45.13.151.19:5003";
        private static readonly string ApiAuthKey = "EVOCRAFTSECURITYPASSWORD2026";
        private static readonly string PlaytimeVpsApiUrl = "http://45.13.151.19:5000/playtime";

        private MSession? _session;
        private MSession? _premiumSession = null;
        private bool _isPremiumMode = false;
        private bool _isRefreshing = false;
        private bool _isInstallingModpack = false;
        private bool _isLaunching = false;
        private bool _isDatabaseLoggedIn = false;
        private bool _isBanned = false;

        private string _pendingUsername = "";
        private string _pendingPassword = "";
        private bool _pendingIsPremium = false;

        private string _currentJoinSecret = "";
        private HttpListener? _httpServer;
        private int _currentApiPort = -1;
        private string _startupToken = "";
        private string _sessionToken = "";
        private bool _handshakeCompleted = false;
        private bool _isHttpRunning = false;
        private string _currentWebToken = "";

        // VARIABILE MENTENANȚĂ
        private bool _isMaintenanceActive = false;
        private bool _isMaintenanceSurvival = false;
        private bool _isMaintenanceSkyblock = false;
        private bool _isMaintenanceCreative = false;
        private bool _isMaintenanceParkour = false;

        private bool _isOwnerBypassActive = false;
        private string OwnerSecretPass = "";

        private string _userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".evocraft");
        private TimeSpan _totalPlayTime = TimeSpan.Zero;
        private DiscordRpcClient? discordClient;
        private static readonly string SecurityKey = "EVO_CRAFT_LAUNCHER_SECURE_2026_BY_MARIUS";
        private List<string> officialMods = new List<string>();

        // VARIABILE VOICE CHAT
        private string VoiceChatModUrl = "";
        private string VoiceChatModFileName = "";
        private bool _isVoiceChatEnabled = false;

        // FUNCTIE PENTRU CURATAREA RAM-ULUI GENERAT DE WPF
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

        public static void FlushMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
                GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (UIntPtr)0xFFFFFFFF, (UIntPtr)0xFFFFFFFF);
                }
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            txtGamePath.Text = _userDataPath;
            lblLauncherVersion.Text = $"VERSIUNE LAUNCHER: v{CurrentLauncherVersion}";
            InitializeDiscord();
            LoadSettings();

            string currentModeFile = Path.Combine(_userDataPath, "current_mode.txt");
            if (File.Exists(currentModeFile))
            {
                string savedMode = File.ReadAllText(currentModeFile).Trim();
                if (savedMode == "Survival" || savedMode == "Skyblock" || savedMode == "Creative" || savedMode == "Parkour")
                {
                    _selectedGameMode = savedMode;
                }
            }

            string vcState = DecryptAndReadFile("voicechat_state.dat");
            _isVoiceChatEnabled = (vcState == "ON");
            UpdateVoiceChatUI();

            UpdateModeButtonsUI();
            InitLauncherSequence();

            Task.Run(async () => { await Task.Delay(2000); FlushMemory(); });
        }

        private void LoadSettings()
        {
            string jvmArgs = DecryptAndReadFile("jvm_args.dat");
            if (!string.IsNullOrEmpty(jvmArgs))
                txtJvmArgs.Text = jvmArgs;
        }

        private async Task LoadTopPlayed()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(PlaytimeVpsApiUrl);
                    var allPlayers = JsonConvert.DeserializeObject<List<PlayerStats>>(json);
                    if (allPlayers != null)
                    {
                        if (_session != null && !string.IsNullOrEmpty(_session.Username))
                        {
                            var currentPlayer = allPlayers.FirstOrDefault(p => p.Username.Equals(_session.Username, StringComparison.OrdinalIgnoreCase));
                            if (currentPlayer != null)
                            {
                                _totalPlayTime = TimeSpan.FromSeconds(currentPlayer.TotalSeconds);
                                UpdatePlaytimeUI();
                            }
                        }
                        Dispatcher.Invoke(() => {
                            lstTopPlayed.ItemsSource = allPlayers.Take(10).ToList();
                        });
                    }
                }
            }
            catch { }
        }

        private void EncryptAndSaveFile(string fileName, string content)
        {
            try
            {
                byte[] clearBytes = Encoding.UTF8.GetBytes(content);
                using (Aes encryptor = Aes.Create())
                {
                    byte[] salt = new byte[] { 0x45, 0x76, 0x6f, 0x43, 0x72, 0x61, 0x66, 0x74, 0x53, 0x61, 0x6c, 0x74 };
                    using (var pdb = new Rfc2898DeriveBytes(SecurityKey, salt, 100_000, HashAlgorithmName.SHA256))
                    {
                        encryptor.Key = pdb.GetBytes(32);
                        encryptor.IV = pdb.GetBytes(16);
                        using (MemoryStream ms = new MemoryStream())
                        {
                            using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                            {
                                cs.Write(clearBytes, 0, clearBytes.Length);
                            }
                            File.WriteAllBytes(Path.Combine(_userDataPath, fileName), ms.ToArray());
                        }
                    }
                }
            }
            catch { }
        }

        private string DecryptAndReadFile(string fileName)
        {
            string path = Path.Combine(_userDataPath, fileName);
            if (!File.Exists(path)) return "";
            try
            {
                byte[] cipherBytes = File.ReadAllBytes(path);
                using (Aes encryptor = Aes.Create())
                {
                    byte[] salt = new byte[] { 0x45, 0x76, 0x6f, 0x43, 0x72, 0x61, 0x66, 0x74, 0x53, 0x61, 0x6c, 0x74 };
                    using (var pdb = new Rfc2898DeriveBytes(SecurityKey, salt, 100_000, HashAlgorithmName.SHA256))
                    {
                        encryptor.Key = pdb.GetBytes(32);
                        encryptor.IV = pdb.GetBytes(16);
                        using (MemoryStream ms = new MemoryStream())
                        {
                            using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                            {
                                cs.Write(cipherBytes, 0, cipherBytes.Length);
                            }
                            return Encoding.UTF8.GetString(ms.ToArray());
                        }
                    }
                }
            }
            catch { return ""; }
        }

        private void InitializeDiscord()
        {
            try
            {
                discordClient = new DiscordRpcClient("1443314390899232929");
                discordClient.Initialize();
                UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
            }
            catch { }
        }

        public void UpdatePresence(string state, string details)
        {
            if (discordClient == null || !discordClient.IsInitialized) return;
            discordClient.SetPresence(new RichPresence()
            {
                Details = details,
                State = state,
                Timestamps = Timestamps.Now,
                Assets = new Assets() { LargeImageKey = "logo", LargeImageText = "EVO CRAFT Official" },
                Buttons = new DiscordRPC.Button[] { new DiscordRPC.Button() { Label = "Alătură-te pe Discord", Url = "https://discord.gg/e9yHMaMwTQ" } }
            });
        }

        private string GetHWID()
        {
            try
            {
                string hwid = "";
                ManagementObjectSearcher mbs = new ManagementObjectSearcher("Select ProcessorId From Win32_Processor");
                foreach (ManagementObject mo in mbs.Get())
                {
                    var procId = mo["ProcessorId"]?.ToString();
                    if (!string.IsNullOrEmpty(procId)) { hwid += procId; break; }
                }
                ManagementObjectSearcher mos = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject mo in mos.Get())
                {
                    var serial = mo["SerialNumber"]?.ToString();
                    if (!string.IsNullOrEmpty(serial)) { hwid += serial; break; }
                }
                return hwid.Replace(" ", "");
            }
            catch { return "UNKNOWN_DEVICE"; }
        }

        private void EncryptAndSaveMods(List<string> mods, string mode)
        {
            EncryptAndSaveFile($"official_mods_{mode.ToLower()}.dat", string.Join("|", mods));
        }

        private List<string> LoadAndDecryptMods(string mode)
        {
            string c = DecryptAndReadFile($"official_mods_{mode.ToLower()}.dat");
            return string.IsNullOrEmpty(c) ? new List<string>() : c.Split('|').ToList();
        }

        private void SaveBypassEncrypted(bool isActive)
        {
            EncryptAndSaveFile("owner_bypass.dat", isActive ? "BYPASS_ENABLED_TRUE" : "BYPASS_DISABLED");
        }

        private bool LoadBypassEncrypted()
        {
            return DecryptAndReadFile("owner_bypass.dat") == "BYPASS_ENABLED_TRUE";
        }

        private void SetSplashStatus(string text)
        {
            Dispatcher.Invoke(() => {
                lblSplashLoadingText.Text = text;
            });
        }

        private async Task CheckGlobalBanAsync(string? username = null)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var payload = new { hwid = GetHWID(), username = username, auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/check-ban", content);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("banned", out var bannedEl) && bannedEl.GetBoolean())
                            {
                                _isBanned = true;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async void InitLauncherSequence()
        {
            if (!Directory.Exists(_userDataPath)) Directory.CreateDirectory(_userDataPath);

            SetSplashStatus("Conectare la serverul de securitate...");
            bool apiResponse = await LoadConfigFromVps();

            if (!apiResponse)
            {
                Dispatcher.Invoke(() => {
                    if (lblSplashLoadingText != null)
                    {
                        lblSplashLoadingText.Text = "VPS OFFLINE - CONTACTEAZĂ UN ADMIN";
                        lblSplashLoadingText.Foreground = Brushes.Red;
                    }
                    if (pbSplashLoading != null) pbSplashLoading.Visibility = Visibility.Collapsed;
                    if (btnExitVps != null) btnExitVps.Visibility = Visibility.Visible;
                });
                return;
            }

            SetSplashStatus("Se verifică statusul securității (HWID)...");
            await CheckGlobalBanAsync();

            _isOwnerBypassActive = LoadBypassEncrypted();
            if (_isMaintenanceActive && !_isOwnerBypassActive)
            {
                Dispatcher.Invoke(() => {
                    lblSplashLoadingText.Text = "SERVER ÎN MENTENANȚĂ GLOBALĂ";
                    lblSplashLoadingText.Foreground = Brushes.Orange;
                    pbSplashLoading.Visibility = Visibility.Collapsed;
                    btnExitVps.Visibility = Visibility.Visible;
                    btnExitVps.Content = "ÎNCHIDE (MENTENANȚĂ)";
                });
                return;
            }

            SetSplashStatus("Se verifică versiunea aplicației...");
            if (!string.IsNullOrEmpty(OnlineLauncherVersion) && OnlineLauncherVersion != CurrentLauncherVersion)
            {
                SetSplashStatus("Update disponibil! Se pornește Updater-ul...");
                await Task.Delay(2000);
                try
                {
                    string updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater EvoCraft.exe");
                    if (File.Exists(updaterPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updaterPath,
                            Arguments = $"\"{LauncherDownloadUrl}\"",
                            UseShellExecute = true,
                            Verb = "runas",
                            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                        });
                        Application.Current.Shutdown();
                        return;
                    }
                    else
                    {
                        MessageBox.Show($"Fișierul 'Updater EvoCraft.exe' lipsește!\nCale: {updaterPath}", "Eroare Update");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Eroare critică updater: " + ex.Message);
                }
            }

            SetSplashStatus("Se verifică setările de administrator...");
            if (_isOwnerBypassActive)
            {
                Dispatcher.Invoke(() => {
                    btnOwnerBypass.Background = Brushes.Gold;
                    btnOwnerBypass.Content = "ACTIV (Apasă STOP)";
                    btnOwnerBypass.Foreground = Brushes.Black;
                });
            }

            SetSplashStatus("Se încarcă baza de date locală...");
            officialMods = LoadAndDecryptMods(_selectedGameMode);

            SetSplashStatus("Se verifică versiunea modpack-ului...");
            await CheckForUpdates();

            SetSplashStatus("Se verifică statusul serverului...");
            await RefreshServerStatus();

            string loginMode = DecryptAndReadFile("login_mode.dat");
            string savedSession = DecryptAndReadFile("db_session.dat");

            if (loginMode == "premium")
            {
                SetSplashStatus("Autentificare automată Microsoft...");
                if (File.Exists(Path.Combine(_userDataPath, "login_cache")))
                {
                    await PerformMicrosoftLogin(true);
                }
            }
            else if (!string.IsNullOrEmpty(savedSession))
            {
                SetSplashStatus("Autentificare automată cont Evo...");
                string[] savedData = savedSession.Split('|');
                if (savedData.Length >= 2)
                {
                    Dispatcher.Invoke(() => {
                        txtUsername.Text = savedData[0];
                        txtPassword.Password = savedData[1];
                    });

                    string success = await VerifyDatabaseUser(savedData[0], savedData[1]);

                    if (success == "NEEDS_REFERRAL")
                    {
                        _pendingUsername = savedData[0];
                        _pendingPassword = savedData[1];
                        _pendingIsPremium = false;
                        Dispatcher.Invoke(() => {
                            SplashScreen.Visibility = Visibility.Collapsed;
                            var pnl = FindName("pnlReferralPrompt") as Grid;
                            if (pnl != null) pnl.Visibility = Visibility.Visible;
                        });
                        return;
                    }
                    else if (success == "SUCCESS")
                    {
                        _pendingUsername = savedData[0];
                        _pendingPassword = savedData[1];
                        _pendingIsPremium = false;
                        FinalizeLoginAndSetup();
                    }
                }
            }

            if (!_isPremiumMode && !_isDatabaseLoggedIn)
            {
                SetSplashStatus("Pregătire interfață utilizator...");
                UpdateCrackedUI();
                await ShowProfile(string.IsNullOrWhiteSpace(txtUsername.Text) ? "MHF_Steve" : txtUsername.Text);
            }

            SetSplashStatus("Launcher pregătit!");
            UpdateLaunchButtonState();
            await Task.Delay(1500);
            Dispatcher.Invoke(() => SplashScreen.Visibility = Visibility.Collapsed);
            FlushMemory();
        }

        private async Task<bool> LoadConfigFromVps()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var payload = new { auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/get-config", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var jobj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                        if (jobj != null)
                        {
                            OnlineLauncherVersion = jobj.Value<string?>("launcher_version") ?? OnlineLauncherVersion;
                            LauncherDownloadUrl = jobj.Value<string?>("launcher_download_url") ?? LauncherDownloadUrl;
                            CurrentModsDownloadUrl = jobj.Value<string?>("mods_url") ?? CurrentModsDownloadUrl;

                            OnlineModpackVersionSurvival = jobj.Value<string?>("modpack_version_survival") ?? OnlineModpackVersionSurvival;
                            ModsDownloadUrlSurvival = jobj.Value<string?>("mods_url_survival") ?? CurrentModsDownloadUrl;

                            OnlineModpackVersionSkyblock = jobj.Value<string?>("modpack_version_skyblock") ?? OnlineModpackVersionSkyblock;
                            ModsDownloadUrlSkyblock = jobj.Value<string?>("mods_url_skyblock") ?? CurrentModsDownloadUrl;

                            OnlineModpackVersionCreative = jobj.Value<string?>("modpack_version_creative") ?? OnlineModpackVersionCreative;
                            ModsDownloadUrlCreative = jobj.Value<string?>("mods_url_creative") ?? CurrentModsDownloadUrl;

                            OnlineModpackVersionParkour = jobj.Value<string?>("modpack_version_parkour") ?? OnlineModpackVersionParkour;
                            ModsDownloadUrlParkour = jobj.Value<string?>("mods_url_parkour") ?? CurrentModsDownloadUrl;

                            ServerIP = jobj.Value<string?>("server_ip") ?? ServerIP;
                            ServerPort = jobj.Value<string?>("server_port") ?? ServerPort;
                            OwnerSecretPass = jobj.Value<string?>("owner_pass") ?? OwnerSecretPass;

                            _isMaintenanceActive = jobj.Value<bool?>("is_maintenance") ?? false;

                            _isMaintenanceSurvival = jobj.Value<bool?>("is_maintenance_survival") ?? false;
                            _isMaintenanceSkyblock = jobj.Value<bool?>("is_maintenance_skyblock") ?? false;
                            _isMaintenanceCreative = jobj.Value<bool?>("is_maintenance_creative") ?? false;
                            _isMaintenanceParkour = jobj.Value<bool?>("is_maintenance_parkour") ?? false;

                            // API URL PENTRU MODUL DE VOICE CHAT
                            VoiceChatModUrl = jobj.Value<string?>("voice_chat_url") ?? VoiceChatModUrl;
                            if (!string.IsNullOrEmpty(VoiceChatModUrl))
                            {
                                try { VoiceChatModFileName = System.IO.Path.GetFileName(new Uri(VoiceChatModUrl).AbsolutePath); } catch { }
                            }
                        }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void UpdateModeButtonsUI()
        {
            Dispatcher.Invoke(() => {
                btnModeSurvival.Background = Brushes.Transparent;
                btnModeSurvival.Foreground = Brushes.Gray;
                btnModeSkyblock.Background = Brushes.Transparent;
                btnModeSkyblock.Foreground = Brushes.Gray;
                btnModeCreative.Background = Brushes.Transparent;
                btnModeCreative.Foreground = Brushes.Gray;
                btnModeParkour.Background = Brushes.Transparent;
                btnModeParkour.Foreground = Brushes.Gray;

                var activeBrush = new SolidColorBrush(Color.FromRgb(67, 181, 129));

                if (_selectedGameMode == "Survival") { btnModeSurvival.Background = activeBrush; btnModeSurvival.Foreground = Brushes.White; }
                else if (_selectedGameMode == "Skyblock") { btnModeSkyblock.Background = activeBrush; btnModeSkyblock.Foreground = Brushes.White; }
                else if (_selectedGameMode == "Creative") { btnModeCreative.Background = activeBrush; btnModeCreative.Foreground = Brushes.White; }
                else if (_selectedGameMode == "Parkour") { btnModeParkour.Background = activeBrush; btnModeParkour.Foreground = Brushes.White; }
            });
        }

        private void tgVoiceChat_Click(object sender, RoutedEventArgs e)
        {
            _isVoiceChatEnabled = !_isVoiceChatEnabled;
            EncryptAndSaveFile("voicechat_state.dat", _isVoiceChatEnabled ? "ON" : "OFF");
            UpdateVoiceChatUI();
        }

        private void UpdateVoiceChatUI()
        {
            Dispatcher.Invoke(() => {
                if (_isVoiceChatEnabled)
                {
                    tgVoiceChat.Content = "🎤 VOICE CHAT: ON";
                    tgVoiceChat.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
                    tgVoiceChat.Foreground = Brushes.White;
                }
                else
                {
                    tgVoiceChat.Content = "🎤 VOICE CHAT: OFF";
                    tgVoiceChat.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                    tgVoiceChat.Foreground = Brushes.Gray;
                }
            });
        }

        private void SwitchFoldersForMode(string targetMode)
        {
            try
            {
                string currentModeFile = Path.Combine(_userDataPath, "current_mode.txt");
                string currentMode = File.Exists(currentModeFile) ? File.ReadAllText(currentModeFile).Trim() : "Survival";

                if (currentMode.Equals(targetMode, StringComparison.OrdinalIgnoreCase))
                    return;

                string[] foldersToSwap = { "mods", "config", "resourcepacks", "fancymenu_data", "shaderpacks" };

                foreach (var folder in foldersToSwap)
                {
                    string activeDir = Path.Combine(_userDataPath, folder);
                    string backupForCurrent = Path.Combine(_userDataPath, $"{folder}_{currentMode.ToLower()}");
                    string targetBackup = Path.Combine(_userDataPath, $"{folder}_{targetMode.ToLower()}");

                    if (Directory.Exists(activeDir))
                    {
                        if (Directory.Exists(backupForCurrent)) Directory.Delete(backupForCurrent, true);
                        Directory.Move(activeDir, backupForCurrent);
                    }

                    if (Directory.Exists(targetBackup))
                    {
                        Directory.Move(targetBackup, activeDir);
                    }
                    else
                    {
                        Directory.CreateDirectory(activeDir);
                    }
                }

                File.WriteAllText(currentModeFile, targetMode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la schimbarea secțiunii! Asigură-te că jocul este oprit complet.\n\nEroare: {ex.Message}", "Eroare Switch");
            }
        }

        private void btnModeSurvival_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstallingModpack || _isLaunching) return;
            if (_isMaintenanceSurvival && !_isOwnerBypassActive)
            {
                MessageBox.Show("Secțiunea Survival este momentan în MENTENANȚĂ!\nTe rugăm să revii mai târziu.", "Mentenanță", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SwitchFoldersForMode("Survival");
            _selectedGameMode = "Survival";
            officialMods = LoadAndDecryptMods("Survival");
            UpdateModeButtonsUI();
            UpdateLaunchButtonState();
            UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
            FlushMemory();
        }

        private void btnModeSkyblock_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstallingModpack || _isLaunching) return;
            if (_isMaintenanceSkyblock && !_isOwnerBypassActive)
            {
                MessageBox.Show("Secțiunea Skyblock este momentan în MENTENANȚĂ!\nTe rugăm să revii mai târziu.", "Mentenanță", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SwitchFoldersForMode("Skyblock");
            _selectedGameMode = "Skyblock";
            officialMods = LoadAndDecryptMods("Skyblock");
            UpdateModeButtonsUI();
            UpdateLaunchButtonState();
            UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
            FlushMemory();
        }

        private void btnModeCreative_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstallingModpack || _isLaunching) return;
            if (_isMaintenanceCreative && !_isOwnerBypassActive)
            {
                MessageBox.Show("Secțiunea Creative este momentan în MENTENANȚĂ!\nTe rugăm să revii mai târziu.", "Mentenanță", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SwitchFoldersForMode("Creative");
            _selectedGameMode = "Creative";
            officialMods = LoadAndDecryptMods("Creative");
            UpdateModeButtonsUI();
            UpdateLaunchButtonState();
            UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
            FlushMemory();
        }

        private void btnModeParkour_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstallingModpack || _isLaunching) return;
            if (_isMaintenanceParkour && !_isOwnerBypassActive)
            {
                MessageBox.Show("Secțiunea Parkour este momentan în MENTENANȚĂ!\nTe rugăm să revii mai târziu.", "Mentenanță", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SwitchFoldersForMode("Parkour");
            _selectedGameMode = "Parkour";
            officialMods = LoadAndDecryptMods("Parkour");
            UpdateModeButtonsUI();
            UpdateLaunchButtonState();
            UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
            FlushMemory();
        }

        private void UpdateLaunchButtonState()
        {
            if (_isInstallingModpack)
            {
                Dispatcher.Invoke(() => { btnLaunch.Content = "SE VERIFICĂ FIȘIERELE"; btnLaunch.IsEnabled = false; });
                return;
            }
            if (_isLaunching)
            {
                Dispatcher.Invoke(() => { btnLaunch.IsEnabled = false; });
                return;
            }

            string targetOnlineVer = "1.0";
            if (_selectedGameMode == "Survival") targetOnlineVer = OnlineModpackVersionSurvival;
            else if (_selectedGameMode == "Skyblock") targetOnlineVer = OnlineModpackVersionSkyblock;
            else if (_selectedGameMode == "Creative") targetOnlineVer = OnlineModpackVersionCreative;
            else if (_selectedGameMode == "Parkour") targetOnlineVer = OnlineModpackVersionParkour;

            string localVerFile = Path.Combine(_userDataPath, $"modpack_version_{_selectedGameMode.ToLower()}.info");

            string localVer = File.Exists(localVerFile) ? File.ReadAllText(localVerFile).Trim() : "";
            bool modsExist = Directory.Exists(Path.Combine(_userDataPath, "mods"));

            Dispatcher.Invoke(() => {
                if (_isBanned)
                {
                    btnLaunch.Content = "Acest PC este BANAT";
                    btnLaunch.Background = Brushes.Red;
                    btnLaunch.IsEnabled = false;
                    return;
                }

                bool isCurrentModeMaintenance = false;
                if (_selectedGameMode == "Survival" && _isMaintenanceSurvival) isCurrentModeMaintenance = true;
                else if (_selectedGameMode == "Skyblock" && _isMaintenanceSkyblock) isCurrentModeMaintenance = true;
                else if (_selectedGameMode == "Creative" && _isMaintenanceCreative) isCurrentModeMaintenance = true;
                else if (_selectedGameMode == "Parkour" && _isMaintenanceParkour) isCurrentModeMaintenance = true;

                if (isCurrentModeMaintenance && !_isOwnerBypassActive)
                {
                    btnLaunch.Content = $"MENTENANȚĂ ({_selectedGameMode})";
                    btnLaunch.Background = Brushes.Orange;
                    btnLaunch.IsEnabled = false;
                    try { if (lblModpackVersion != null) lblModpackVersion.Text = $"MODPACK: OFFLINE ({_selectedGameMode})"; } catch { }
                    return;
                }

                if (!_isPremiumMode && !_isDatabaseLoggedIn)
                {
                    btnLaunch.Content = "LOGIN LAUNCHER";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(114, 137, 218));
                }
                else if (!modsExist || string.IsNullOrEmpty(localVer))
                {
                    btnLaunch.Content = $"INSTALEAZĂ ({_selectedGameMode})";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(250, 166, 26));
                }
                else if (localVer != targetOnlineVer)
                {
                    btnLaunch.Content = $"UPDATE ({_selectedGameMode})";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(114, 137, 218));
                }
                else
                {
                    btnLaunch.Content = $"JOACĂ ({_selectedGameMode})";
                    btnLaunch.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
                }

                try { if (lblModpackVersion != null) lblModpackVersion.Text = $"MODPACK: v{targetOnlineVer} ({_selectedGameMode})"; } catch { }
                btnLaunch.IsEnabled = true;
            });
        }

        private async Task RefreshServerStatus()
        {
            try
            {
                ushort port = ushort.TryParse(ServerPort, out var p) ? p : (ushort)25565;
                var status = await ServerListClient.GetStatusAsync(ServerIP, port);
                Dispatcher.Invoke(() => {
                    string motd = (status.Description ?? string.Empty).Trim();
                    int online = status.Players.Online;
                    int max = status.Players.Max;
                    if (!string.IsNullOrWhiteSpace(motd) || online > 0 || max > 0)
                    {
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
                    lblServerInfo.Text = "SERVER OFFLINE";
                    elServerStatus.Fill = Brushes.Red;
                });
            }
        }

        private async void btnRefreshServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rotate = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(600)))
                {
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseInOut }
                };
                if (btnRefreshServer.RenderTransform == null || !(btnRefreshServer.RenderTransform is RotateTransform))
                {
                    btnRefreshServer.RenderTransform = new RotateTransform(0);
                }
                btnRefreshServer.RenderTransformOrigin = new Point(0.5, 0.5);
                (btnRefreshServer.RenderTransform as RotateTransform)?.BeginAnimation(RotateTransform.AngleProperty, rotate);
            }
            catch { }

            if (_isRefreshing) return;
            _isRefreshing = true;
            lblServerInfo.Text = "VERIFICARE...";
            await RefreshServerStatus();
            _isRefreshing = false;
        }

        private async Task<bool> UpdateHwidInDatabase(string user)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var payload = new { username = user, hwid = GetHWID(), auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/update-hwid", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                        {
                            var root = doc.RootElement;
                            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;

                            if (status == "BANNED_HWID")
                            {
                                return false;
                            }
                        }
                    }
                    return true;
                }
            }
            catch { return true; }
        }

        private async Task<string> VerifyDatabaseUser(string user, string pass)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var payload = new { user = user.Trim(), pass = pass.Trim(), hwid = GetHWID(), auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/login", content);

                    if (response.IsSuccessStatusCode)
                    {
                        string contentResult = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(contentResult))
                        {
                            var root = doc.RootElement;
                            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;

                            if (string.Equals(status, "BANNED", StringComparison.OrdinalIgnoreCase)) return "BANNED";

                            if (string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                            {
                                if (root.TryGetProperty("player", out var player))
                                {
                                    if (player.TryGetProperty("is_premium", out var isPremEl))
                                    {
                                        bool isPremium = (isPremEl.ValueKind == JsonValueKind.True) ||
                                                         (isPremEl.ValueKind == JsonValueKind.String && isPremEl.GetString()?.ToUpper() == "TRUE");

                                        if (isPremium) return "PREMIUM_BLOCK";
                                    }

                                    if (player.TryGetProperty("has_referral", out var refEl))
                                    {
                                        bool hasReferral = refEl.ValueKind == JsonValueKind.True || (refEl.ValueKind == JsonValueKind.Number && refEl.GetInt32() == 1);
                                        if (!hasReferral) return "NEEDS_REFERRAL";
                                    }

                                    var savedHwid = player.TryGetProperty("hwid", out var hwidEl) ? hwidEl.GetString() : null;

                                    if (savedHwid == "WEB_REGISTRATION")
                                    {
                                        bool hwidUpdateSuccess = await UpdateHwidInDatabase(user.Trim());
                                        if (!hwidUpdateSuccess) return "BANNED";
                                    }
                                    else if (!string.IsNullOrEmpty(savedHwid) && savedHwid != GetHWID() && !_isOwnerBypassActive)
                                    {
                                        return "HWID_LOCK";
                                    }

                                    if (player.TryGetProperty("playtime_seconds", out JsonElement ptElement) && ptElement.ValueKind == JsonValueKind.Number)
                                    {
                                        _totalPlayTime = TimeSpan.FromSeconds(ptElement.GetInt64());
                                        UpdatePlaytimeUI();
                                    }
                                    return "SUCCESS";
                                }
                            }
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return "USER_NOT_FOUND";
                    }
                }
            }
            catch { }
            return "ERROR";
        }

        private async Task<string> SyncPremiumUserWithDb(string? username, string? uuid)
        {
            if (string.IsNullOrWhiteSpace(username)) return "ERROR";
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string finalUuid = uuid ?? "UNKNOWN_UUID";

                    var payload = new
                    {
                        username = username,
                        password = "PREMIUM_ACCOUNT",
                        hwid = GetHWID(),
                        auth_key = ApiAuthKey,
                        is_premium = true,
                        microsoft_id = finalUuid
                    };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{VpsApiUrl}/register", content);

                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (json.Contains("BANNED_HWID")) return "BANNED";
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("player", out var player))
                            {
                                if (player.TryGetProperty("has_referral", out var refEl))
                                {
                                    bool hasReferral = refEl.ValueKind == JsonValueKind.True || (refEl.ValueKind == JsonValueKind.Number && refEl.GetInt32() == 1);
                                    if (!hasReferral) return "NEEDS_REFERRAL";
                                }
                            }
                        }
                    }
                    await UpdateHwidInDatabase(username);
                    return "SUCCESS";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Eroare sync Premium DB: " + ex.Message);
                return "ERROR";
            }
        }

        private async Task<string> SubmitReferralCode(string username, string code)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var payload = new { username = username, referral_code = code, hwid = GetHWID(), auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/set-referral", content);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            return doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "ERROR" : "ERROR";
                        }
                    }
                }
            }
            catch { }
            return "ERROR";
        }

        private async void btnSubmitReferral_Click(object sender, RoutedEventArgs e)
        {
            string code = (FindName("txtReferralCode") as TextBox)?.Text.Trim() ?? "";
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("Introdu un cod valid sau apasă Sari Peste!");
                return;
            }

            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var checkPayload = new { code = code, auth_key = ApiAuthKey };
                    var checkContent = new StringContent(JsonConvert.SerializeObject(checkPayload), Encoding.UTF8, "application/json");
                    var checkResponse = await client.PostAsync($"{VpsApiUrl}/check-referral", checkContent);

                    if (checkResponse.IsSuccessStatusCode)
                    {
                        var checkJson = await checkResponse.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(checkJson))
                        {
                            string status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                            if (status == "VALID")
                            {
                                string refName = doc.RootElement.GetProperty("username").GetString() ?? "";
                                var msgResult = MessageBox.Show($"Jucătorul '{refName}' te-a adus pe server?\n\nDacă da, apasă YES pentru a aplica codul.", "Confirmare Invitație", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                if (msgResult == MessageBoxResult.No)
                                {
                                    if (btn != null) btn.IsEnabled = true;
                                    return;
                                }
                            }
                            else
                            {
                                MessageBox.Show("Acest ID de referral NU EXISTĂ!", "Eroare", MessageBoxButton.OK, MessageBoxImage.Error);
                                if (btn != null) btn.IsEnabled = true;
                                return;
                            }
                        }
                    }
                }
            }
            catch { }

            string result = await SubmitReferralCode(_pendingUsername, code);

            if (btn != null) btn.IsEnabled = true;

            if (result == "SUCCESS")
            {
                MessageBox.Show("Cod aplicat cu succes! Primești bonusul în joc.");
                Dispatcher.Invoke(() => {
                    var pnl = FindName("pnlReferralPrompt") as Grid;
                    if (pnl != null) pnl.Visibility = Visibility.Collapsed;
                });
                FinalizeLoginAndSetup();
                await ProceedToLaunch(_pendingUsername, _pendingPassword, _pendingIsPremium);
            }
            else if (result == "INVALID_CODE")
            {
                MessageBox.Show("Acest cod de referral nu există!");
            }
            else if (result == "OWN_CODE")
            {
                MessageBox.Show("Nu poți folosi propriul tău cod sau un cod de pe același PC!");
            }
            else
            {
                MessageBox.Show("Eroare la serverul API.");
            }
        }

        private async void btnSkipReferral_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            await SubmitReferralCode(_pendingUsername, "SKIP");

            if (btn != null) btn.IsEnabled = true;

            Dispatcher.Invoke(() => {
                var pnl = FindName("pnlReferralPrompt") as Grid;
                if (pnl != null) pnl.Visibility = Visibility.Collapsed;
            });
            FinalizeLoginAndSetup();
            await ProceedToLaunch(_pendingUsername, _pendingPassword, _pendingIsPremium);
        }

        private async void FinalizeLoginAndSetup()
        {
            if (!_pendingIsPremium)
            {
                _isDatabaseLoggedIn = true;
                _session = MSession.CreateOfflineSession(_pendingUsername);
                EncryptAndSaveFile("db_session.dat", $"{_pendingUsername}|{_pendingPassword}");
                UpdateDatabaseUI(true);
            }
            else
            {
                _isPremiumMode = true;
                _session = _premiumSession;
                UpdatePremiumUI();
                await ShowProfile(_premiumSession?.Username);
            }
            await LoadTopPlayed();
            UpdateLaunchButtonState();
        }

        private async void btnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (_isLaunching) return;

            await CheckGlobalBanAsync(txtUsername.Text);
            if (_isBanned)
            {
                UpdateLaunchButtonState();
                MessageBox.Show("Acest calculator sau cont a fost BANAT!", "INTERZIS", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_isPremiumMode && !_isDatabaseLoggedIn)
            {
                if (string.IsNullOrWhiteSpace(txtUsername.Text) || string.IsNullOrWhiteSpace(txtPassword.Password))
                {
                    MessageBox.Show("Introdu numele și parola!");
                    return;
                }
                lblStatus.Text = "Se verifică contul...";

                string loginResult = await VerifyDatabaseUser(txtUsername.Text, txtPassword.Password);

                if (loginResult == "BANNED")
                {
                    _isBanned = true;
                    UpdateLaunchButtonState();
                    MessageBox.Show("Cont BANAT!", "INTERZIS");
                    lblStatus.Text = "Acces respins";
                    return;
                }
                else if (loginResult == "NEEDS_REFERRAL")
                {
                    _pendingUsername = txtUsername.Text;
                    _pendingPassword = txtPassword.Password;
                    _pendingIsPremium = false;
                    Dispatcher.Invoke(() => {
                        var pnl = FindName("pnlReferralPrompt") as Grid;
                        if (pnl != null) pnl.Visibility = Visibility.Visible;
                    });
                    return; // Așteptăm input
                }
                else if (loginResult == "SUCCESS")
                {
                    _pendingUsername = txtUsername.Text;
                    _pendingPassword = txtPassword.Password;
                    _pendingIsPremium = false;
                    FinalizeLoginAndSetup();
                    lblStatus.Text = "Login reușit!";
                }
                else if (loginResult == "PREMIUM_BLOCK")
                {
                    MessageBox.Show("Nume Premium! Loghează-te prin Microsoft.");
                    lblStatus.Text = "Cont Premium detectat!";
                    return;
                }
                else if (loginResult == "USER_NOT_FOUND")
                {
                    lblStatus.Text = "Nu există contul";
                    lblStatus.Foreground = Brushes.Red;
                    return;
                }
                else if (loginResult == "HWID_LOCK")
                {
                    MessageBox.Show("Eroare: Cont blocat pe alt PC!", "HWID");
                    return;
                }
                else
                {
                    lblStatus.Text = "Eroare DB";
                    return;
                }
            }
            else
            {
                _pendingUsername = _isPremiumMode ? _premiumSession?.Username ?? "" : txtUsername.Text;
                _pendingPassword = txtPassword.Password;
                _pendingIsPremium = _isPremiumMode;
            }

            await ProceedToLaunch(_pendingUsername, _pendingPassword, _pendingIsPremium);
        }

        private string CalculateMD5(string filename)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch { return ""; }
        }

        private async Task<bool> HandleModpackSync()
        {
            string targetOnlineVer = "1.0";
            string targetModsUrl = "";

            if (_selectedGameMode == "Survival") { targetOnlineVer = OnlineModpackVersionSurvival; targetModsUrl = ModsDownloadUrlSurvival; }
            else if (_selectedGameMode == "Skyblock") { targetOnlineVer = OnlineModpackVersionSkyblock; targetModsUrl = ModsDownloadUrlSkyblock; }
            else if (_selectedGameMode == "Creative") { targetOnlineVer = OnlineModpackVersionCreative; targetModsUrl = ModsDownloadUrlCreative; }
            else if (_selectedGameMode == "Parkour") { targetOnlineVer = OnlineModpackVersionParkour; targetModsUrl = ModsDownloadUrlParkour; }

            string localVerFile = Path.Combine(_userDataPath, $"modpack_version_{_selectedGameMode.ToLower()}.info");
            string localVer = File.Exists(localVerFile) ? File.ReadAllText(localVerFile).Trim() : "";

            if (!Directory.Exists(Path.Combine(_userDataPath, "mods")) || localVer != targetOnlineVer)
            {
                if (string.IsNullOrWhiteSpace(targetModsUrl))
                {
                    MessageBox.Show($"Link-ul manifestului JSON pentru {_selectedGameMode} este GOL!\nVerifică API-ul sau version.txt.", "Eroare Descărcare", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                _isInstallingModpack = true;
                Dispatcher.Invoke(() => { btnLaunch.Content = "SE VERIFICĂ FIȘIERELE"; btnLaunch.IsEnabled = false; lblStatus.Text = "SINCRONIZARE UPDATE..."; });
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        var response = await client.GetStringAsync(targetModsUrl);
                        var manifest = JsonConvert.DeserializeObject<ModpackManifest>(response);

                        if (manifest != null && manifest.Files != null)
                        {
                            int totalTasks = manifest.Files.Count;
                            int completedTasks = 0;

                            foreach (var fileInfo in manifest.Files)
                            {
                                string localFilePath = Path.Combine(_userDataPath, fileInfo.Path.Replace('/', '\\'));
                                string localDir = Path.GetDirectoryName(localFilePath);
                                if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                                    Directory.CreateDirectory(localDir);

                                bool needsDownload = true;
                                if (File.Exists(localFilePath))
                                {
                                    string localHash = CalculateMD5(localFilePath);
                                    if (localHash.Equals(fileInfo.Hash, StringComparison.OrdinalIgnoreCase))
                                    {
                                        needsDownload = false;
                                    }
                                }

                                if (needsDownload)
                                {
                                    Dispatcher.Invoke(() => lblProgressDetails.Text = $"Descărcare: {Path.GetFileName(fileInfo.Path)}");

                                    var fileResp = await client.GetAsync(fileInfo.Url, HttpCompletionOption.ResponseHeadersRead);
                                    using (var fs = new FileStream(localFilePath, FileMode.Create))
                                    using (var stream = await fileResp.Content.ReadAsStreamAsync())
                                    {
                                        var buffer = new byte[8192];
                                        int read;
                                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                        {
                                            await fs.WriteAsync(buffer, 0, read);
                                        }
                                    }
                                }

                                completedTasks++;
                                Dispatcher.Invoke(() => {
                                    pbProgress.Value = ((double)completedTasks / totalTasks) * 100;
                                    lblStatus.Text = $"VERIFICARE... {completedTasks}/{totalTasks}";
                                });
                            }

                            // --- CURĂȚARE STRICTĂ: ȘTERGEM TOT CE NU E ÎN MANIFEST (Moduri, Config-uri vechi, etc) ---
                            var validAbsolutePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var f in manifest.Files)
                            {
                                validAbsolutePaths.Add(Path.GetFullPath(Path.Combine(_userDataPath, f.Path.Replace('/', '\\'))));
                            }

                            string[] foldersToCleanStrict = { "mods", "config", "resourcepacks", "fancymenu_data", "shaderpacks" };
                            foreach (var folder in foldersToCleanStrict)
                            {
                                string dirPath = Path.Combine(_userDataPath, folder);
                                if (Directory.Exists(dirPath))
                                {
                                    string[] existingFiles = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
                                    foreach (var existingFile in existingFiles)
                                    {
                                        if (!validAbsolutePaths.Contains(Path.GetFullPath(existingFile)))
                                        {
                                            try { File.Delete(existingFile); } catch { }
                                        }
                                    }
                                }
                            }
                            // ------------------------------------------------------------------------------------------
                        }

                        officialMods.Clear();
                        string finalModsPath = Path.Combine(_userDataPath, "mods");
                        if (Directory.Exists(finalModsPath))
                        {
                            officialMods.AddRange(Directory.GetFiles(finalModsPath, "*.jar").Select(p => Path.GetFileName(p) ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)));
                            EncryptAndSaveMods(officialMods, _selectedGameMode);
                        }
                        File.WriteAllText(localVerFile, targetOnlineVer);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Eroare la descărcarea manifestului {_selectedGameMode}!\nLink accesat: {targetModsUrl}\n\nEroare: {ex.Message}", "Eroare Update");
                    return false;
                }
                finally
                {
                    _isInstallingModpack = false;
                    UpdateLaunchButtonState();
                    Dispatcher.Invoke(() => { lblStatus.Text = ""; lblProgressDetails.Text = ""; });
                    FlushMemory();
                }
            }

            // =========================================================================
            // GESTIONARE MOD VOICE CHAT (SE EXECUTĂ DUPĂ CURĂȚARE CA SĂ NU FIE ȘTERS)
            // =========================================================================
            if (string.IsNullOrEmpty(VoiceChatModFileName)) VoiceChatModFileName = "voicechat-forge-1.20.1-2.6.14.jar";
            string voiceChatFile = Path.Combine(_userDataPath, "mods", VoiceChatModFileName);

            if (_isVoiceChatEnabled && !string.IsNullOrEmpty(VoiceChatModUrl))
            {
                Dispatcher.Invoke(() => {
                    lblStatus.Text = "DESCĂRCARE VOICE CHAT...";
                    lblProgressDetails.Text = "Voice Chat Mod";
                });
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        var vcResp = await client.GetAsync(VoiceChatModUrl);
                        using (var fs = new FileStream(voiceChatFile, FileMode.Create))
                        using (var stream = await vcResp.Content.ReadAsStreamAsync())
                        {
                            await stream.CopyToAsync(fs);
                        }
                    }
                    if (!officialMods.Contains(VoiceChatModFileName))
                    {
                        officialMods.Add(VoiceChatModFileName);
                        EncryptAndSaveMods(officialMods, _selectedGameMode);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Eroare la descărcarea modului Voice Chat: " + ex.Message, "Eroare Voice Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                if (File.Exists(voiceChatFile))
                {
                    try { File.Delete(voiceChatFile); } catch { }
                    if (officialMods.Contains(VoiceChatModFileName))
                    {
                        officialMods.Remove(VoiceChatModFileName);
                        EncryptAndSaveMods(officialMods, _selectedGameMode);
                    }
                }
            }

            return true;
        }

        private async Task ProceedToLaunch(string? username, string? password, bool isPremium)
        {
            _session = isPremium ? _premiumSession : MSession.CreateOfflineSession(username);
            if (_session == null) return;

            _isLaunching = true;
            Dispatcher.Invoke(() => {
                btnLaunch.IsEnabled = false;
                btnLaunch.Content = "SE PREGĂTEȘTE...";
            });
            EncryptAndSaveFile("jvm_args.dat", txtJvmArgs.Text);

            try
            {
                bool syncSuccess = await HandleModpackSync();
                if (!syncSuccess)
                {
                    _isLaunching = false;
                    Dispatcher.Invoke(() => {
                        btnLaunch.IsEnabled = true;
                        UpdateLaunchButtonState();
                        lblStatus.Foreground = Brushes.Gray;
                        lblStatus.Text = "Pornire anulată (Eroare Modpack)";
                    });
                    return;
                }

                CleanFoldersBeforeStart();

                var launcher = new MinecraftLauncher(new MinecraftPath(_userDataPath));
                Dispatcher.Invoke(() => { btnLaunch.Content = "PORNEȘTE JOCUL..."; });

                await launcher.InstallAsync("1.20.1", new Progress<InstallerProgressChangedEventArgs>(ev => {
                    Dispatcher.Invoke(() => {
                        pbProgress.Value = (double)ev.ProgressedTasks / ev.TotalTasks * 100;
                        lblProgressDetails.Text = $"{ev.Name} ({ev.ProgressedTasks}/{ev.TotalTasks})";
                    });
                }), new Progress<ByteProgress>());

                var forgeID = await new ForgeInstaller(launcher).Install("1.20.1", "47.3.0");

                int apiPort = EnsureHttpServerRunning();

                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var random = new Random();
                _startupToken = new string(Enumerable.Repeat(chars, 20).Select(s => s[random.Next(s.Length)]).ToArray());
                _sessionToken = new string(Enumerable.Repeat(chars, 20).Select(s => s[random.Next(s.Length)]).ToArray());
                _handshakeCompleted = false;

                int ramMb = 4096;
                var jvmArgsList = new List<string>();

                // =========================================================================================
                // MAGIA ANTI-INJECT: Argumentele astea refuza orice DLL / Agent / JNI extern direct din Java!
                // Zero RAM consumat de launcher, pentru ca JVM-ul isi face singur treaba.
                // =========================================================================================
                jvmArgsList.Add("-Dlauncher.hwid=" + GetHWID());
                jvmArgsList.Add("-Dsun.java2d.noddraw=true");
                jvmArgsList.Add("-XX:+DisableAttachMechanism"); // Blocheaza programele externe din a se atasa
                jvmArgsList.Add("-XX:-UsePerfData");
                jvmArgsList.Add("-Xcheck:jni"); // Verifica riguros orice injectare de DLL
                jvmArgsList.Add("-Dcom.sun.management.jmxremote=false");
                jvmArgsList.Add("-Djava.rmi.server.disable=true");

                if (apiPort > 0)
                {
                    jvmArgsList.Add($"-Dipc.port={apiPort}");
                    jvmArgsList.Add($"-Dipc.token={_startupToken}");
                }

                if (!string.IsNullOrWhiteSpace(txtJvmArgs.Text))
                {
                    var rawArgs = txtJvmArgs.Text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var arg in rawArgs)
                    {
                        string trimmedArg = arg.Trim();
                        if (trimmedArg.ToLower().StartsWith("-xmx"))
                        {
                            string val = trimmedArg.Substring(4).ToLower().Replace("g", "").Replace("m", "").Replace("b", "");
                            if (int.TryParse(val, out int parsed))
                            {
                                ramMb = trimmedArg.ToLower().Contains("g") ? parsed * 1024 : parsed;
                            }
                            continue;
                        }

                        if (!string.IsNullOrEmpty(trimmedArg) && !string.Equals(trimmedArg, "string", StringComparison.OrdinalIgnoreCase))
                        {
                            jvmArgsList.Add(trimmedArg);
                        }
                    }
                }

                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = ramMb,
                    Session = _session,
                    ExtraJvmArguments = jvmArgsList.Select(x => new MArgument(x)).ToArray()
                };

                var process = await launcher.CreateProcessAsync(forgeID, launchOption);

                // ASCUNDEM LAUNCHER-UL SI ELIBERAM VIDEO-UL (AICI RECUPERAM SUTE DE MB!)
                Dispatcher.Invoke(() => {
                    try { bgVideo.Source = null; } catch { }
                    this.Hide();
                });
                FlushMemory(); // Aruncam gunoiul

                UpdatePresence($"În Joc - {_selectedGameMode}", $"Jucător: {_session.Username}");

                var playStart = DateTime.UtcNow;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardError = true;

                process.StartInfo.EnvironmentVariables.Remove("_JAVA_OPTIONS");
                process.StartInfo.EnvironmentVariables.Remove("JAVA_TOOL_OPTIONS");
                process.StartInfo.EnvironmentVariables.Remove("JDK_JAVA_OPTIONS");

                process.Start();

                // CITIM ERORILE: Aici "prindem" cheat-urile cand incearca sa se bage si Java le da Crash.
                StringBuilder err = new StringBuilder();
                process.ErrorDataReceived += (s, evData) => { if (evData.Data != null) err.AppendLine(evData.Data); };
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                int exitCode = 0;
                try { exitCode = process.ExitCode; } catch { }
                string errorOutput = err.ToString();

                bool suspectedCheatCrash = false;

                // Cautam log-urile de crash facute de Java. Daca exista unul in ultimele 2 minute, sigur a fost ceva in neregula!
                try
                {
                    string workingDir = _userDataPath;
                    string[] crashFiles = Directory.GetFiles(workingDir, "hs_err_pid*.log");
                    foreach (var file in crashFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        if ((DateTime.Now - fileInfo.CreationTime).TotalMinutes < 2)
                        {
                            suspectedCheatCrash = true;
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch { }

                if (exitCode != 0)
                {
                    if (exitCode == -1073741819 || exitCode == unchecked((int)0xC0000005))
                    {
                        suspectedCheatCrash = true;
                    }

                    if (errorOutput.Contains("FATAL ERROR in native method") ||
                        errorOutput.Contains("EXCEPTION_ACCESS_VIOLATION") ||
                        errorOutput.Contains("SIGSEGV") ||
                        errorOutput.Contains("JNI DETECTED") ||
                        errorOutput.Contains("JNI panic") ||
                        errorOutput.Contains("corrupted"))
                    {
                        suspectedCheatCrash = true;
                    }
                }

                if (suspectedCheatCrash)
                {
                    Dispatcher.Invoke(() => {
                        try { bgVideo.Source = new Uri("background.mp4", UriKind.RelativeOrAbsolute); bgVideo.Play(); } catch { }
                        this.Show();
                        MessageBox.Show("Cheat detectat!\nMotiv: Tentativă de injectare în memoria Java blocată (JNI / Manual Mapping).\n\nJocul a fost închis forțat de sistemul de securitate.", "SECURITATE EVO CRAFT", MessageBoxButton.OK, MessageBoxImage.Error);
                    });

                    _isLaunching = false;
                    Dispatcher.Invoke(() => {
                        btnLaunch.IsEnabled = true;
                        UpdateLaunchButtonState();
                        lblStatus.Foreground = Brushes.Gray;
                    });
                    return;
                }

                var sessionTime = DateTime.UtcNow - playStart;

                if (exitCode != 0)
                {
                    string[] errorLines = errorOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string shortError = "";
                    if (errorLines.Length > 10)
                    {
                        shortError = "...\n" + string.Join("\n", errorLines.Skip(errorLines.Length - 10));
                    }
                    else
                    {
                        shortError = errorOutput;
                    }

                    Dispatcher.Invoke(() => {
                        try { bgVideo.Source = new Uri("background.mp4", UriKind.RelativeOrAbsolute); bgVideo.Play(); } catch { }
                        this.Show();
                        MessageBox.Show($"JOCUL A CRĂPAT SAU S-A ÎNCHIS ANORMAL!\n\nPosibile motive:\n1. Alocare RAM greșită (pune -Xmx4G).\n2. Un mod a întâmpinat o eroare internă.\n\nDetalii (Ultimele rânduri):\nExit Code: {exitCode}\n{shortError}", "CRASH");
                    });
                }
                else
                {
                    UpdatePresence("În Launcher", $"Pregătire joc ({_selectedGameMode})");
                    _totalPlayTime = _totalPlayTime.Add(sessionTime);
                    UpdatePlaytimeUI();
                    await SyncPlaytimeToDatabase();
                    await Task.Delay(1500);
                    await LoadTopPlayed();
                    Dispatcher.Invoke(() => {
                        try { bgVideo.Source = new Uri("background.mp4", UriKind.RelativeOrAbsolute); bgVideo.Play(); } catch { }
                        this.Show();
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    try { bgVideo.Source = new Uri("background.mp4", UriKind.RelativeOrAbsolute); bgVideo.Play(); } catch { }
                    this.Show();
                    MessageBox.Show("Eroare la pornire: " + ex.Message);
                });
            }
            finally
            {
                _isLaunching = false;
                Dispatcher.Invoke(() => {
                    btnLaunch.IsEnabled = true;
                    UpdateLaunchButtonState();
                    lblStatus.Foreground = Brushes.Gray;
                });
                FlushMemory();
            }
        }

        private async void btnConfirmRegister_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(regUsername.Text) || string.IsNullOrWhiteSpace(regPassword.Password))
            {
                lblRegStatus.Text = "Completează datele!";
                return;
            }

            btnConfirmRegister.IsEnabled = false;
            lblRegStatus.Text = "Se verifică...";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string? referral = (FindName("regReferral") as TextBox)?.Text.Trim();

                    if (!string.IsNullOrWhiteSpace(referral))
                    {
                        var checkPayload = new { code = referral, auth_key = ApiAuthKey };
                        var checkContent = new StringContent(JsonConvert.SerializeObject(checkPayload), Encoding.UTF8, "application/json");
                        var checkResponse = await client.PostAsync($"{VpsApiUrl}/check-referral", checkContent);

                        if (checkResponse.IsSuccessStatusCode)
                        {
                            var checkJson = await checkResponse.Content.ReadAsStringAsync();
                            using (JsonDocument doc = JsonDocument.Parse(checkJson))
                            {
                                string status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                                if (status == "VALID")
                                {
                                    string refName = doc.RootElement.GetProperty("username").GetString() ?? "";
                                    var msgResult = MessageBox.Show($"Jucătorul '{refName}' te-a adus pe server?\n\nDacă da, apasă YES pentru a crea contul.", "Confirmare Invitație", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                    if (msgResult == MessageBoxResult.No)
                                    {
                                        lblRegStatus.Text = "Înregistrare anulată.";
                                        btnConfirmRegister.IsEnabled = true;
                                        return;
                                    }
                                }
                                else
                                {
                                    MessageBox.Show("Acest ID de referral NU EXISTĂ!", "Eroare", MessageBoxButton.OK, MessageBoxImage.Error);
                                    lblRegStatus.Text = "Cod invalid!";
                                    btnConfirmRegister.IsEnabled = true;
                                    return;
                                }
                            }
                        }
                    }

                    var userData = new
                    {
                        username = regUsername.Text,
                        password = regPassword.Password,
                        discord_user = string.IsNullOrWhiteSpace(regDiscord.Text) ? null : regDiscord.Text,
                        hwid = GetHWID(),
                        auth_key = ApiAuthKey,
                        referral_code = string.IsNullOrWhiteSpace(referral) ? null : referral
                    };

                    var content = new StringContent(JsonConvert.SerializeObject(userData), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/register", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            string status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

                            if (status == "SUCCESS")
                            {
                                MessageBox.Show("Cont creat cu succes! Te poți loga acum.");
                                pnlRegister.Visibility = Visibility.Collapsed;
                                txtUsername.Text = regUsername.Text;
                            }
                            else if (status == "BANNED_HWID")
                            {
                                MessageBox.Show("Acest calculator este BANAT pe un alt cont!\nNu poți crea conturi noi.", "INTERZIS", MessageBoxButton.OK, MessageBoxImage.Error);
                                lblRegStatus.Text = "Calculator Banat!";
                            }
                            else if (status == "USER_EXISTS")
                            {
                                lblRegStatus.Text = "Acest nume este deja folosit!";
                            }
                            else if (status == "INVALID_REFERRAL")
                            {
                                MessageBox.Show("Acest cod de referral nu există!", "Eroare", MessageBoxButton.OK, MessageBoxImage.Error);
                                lblRegStatus.Text = "Cod referral invalid!";
                            }
                            else if (status == "OWN_CODE")
                            {
                                MessageBox.Show("Nu poți folosi propriul tău cod sau un cod de pe același PC!", "Eroare", MessageBoxButton.OK, MessageBoxImage.Error);
                                lblRegStatus.Text = "Cod referral invalid!";
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        MessageBox.Show("Acces interzis (HWID Banned)!", "INTERZIS", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        lblRegStatus.Text = "Eroare (VPS Offline sau problemă DB)!";
                    }
                }
            }
            catch { lblRegStatus.Text = "Eroare conexiune!"; }
            finally { btnConfirmRegister.IsEnabled = true; }
        }

        private int EnsureHttpServerRunning()
        {
            if (_isHttpRunning && _httpServer != null && _httpServer.IsListening)
            {
                return _currentApiPort;
            }

            try
            {
                StopHttpServer();

                TcpListener l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                _currentApiPort = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();

                _httpServer = new HttpListener();
                _httpServer.Prefixes.Add($"http://127.0.0.1:{_currentApiPort}/");
                _httpServer.Start();
                _isHttpRunning = true;

                _ = Task.Run(ListenForHttpRequestsAsync);

                return _currentApiPort;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Eroare pornire HTTP Server: " + ex.Message);
                return -1;
            }
        }

        private void StopHttpServer()
        {
            _isHttpRunning = false;
            try
            {
                if (_httpServer != null)
                {
                    _httpServer.Abort();
                }
            }
            catch { }
            finally
            {
                _httpServer = null;
            }
        }

        private async Task ListenForHttpRequestsAsync()
        {
            while (_isHttpRunning && _httpServer != null && _httpServer.IsListening)
            {
                try
                {
                    var context = await _httpServer.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequest(context));
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Debug.WriteLine("Eroare ascultare HTTP: " + ex.Message); }
            }
        }

        private async Task HandleHttpRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "";
                string queryToken = context.Request.QueryString["token"] ?? "";
                string responseString = "INVALID";

                if (path == "/handshake")
                {
                    if (queryToken == _startupToken && !_handshakeCompleted)
                    {
                        _handshakeCompleted = true;
                        responseString = "OK|" + _sessionToken;
                    }
                    else
                    {
                        responseString = "REJECT|TOKEN_INVALID_SAU_FOLOSIT";
                    }
                }
                else if (path == "/challenge")
                {
                    string challengeStr = context.Request.QueryString["c"] ?? "";

                    if (queryToken == _sessionToken && _handshakeCompleted)
                    {
                        bool isValid = await SetChallengeAsSecretInDb(_session?.Username ?? "", challengeStr);
                        responseString = isValid ? "SUCCESS" : "FAILED_API";
                    }
                    else
                    {
                        responseString = "FAILED_UNAUTHORIZED";
                    }
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Eroare prelucrare HTTP: " + ex.Message);
                context.Response.StatusCode = 500;
            }
            finally
            {
                try { context.Response.OutputStream.Close(); } catch { }
            }
        }

        private async Task<bool> SetChallengeAsSecretInDb(string username, string challenge)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(challenge)) return false;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var payload = new
                    {
                        username = username,
                        secret = challenge,
                        auth_key = ApiAuthKey
                    };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{VpsApiUrl}/set-secret", content);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task SyncPlaytimeToDatabase()
        {
            if ((!_isDatabaseLoggedIn && !_isPremiumMode) || _session == null) return;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var updateData = new
                    {
                        username = _session.Username,
                        playtime_seconds = (long)_totalPlayTime.TotalSeconds,
                        auth_key = ApiAuthKey
                    };
                    var content = new StringContent(JsonConvert.SerializeObject(updateData), Encoding.UTF8, "application/json");
                    await client.PostAsync($"{VpsApiUrl}/sync-playtime", content);
                }
            }
            catch { }
        }

        private void btnOpenSecretBypass_Click(object sender, RoutedEventArgs e)
        {
            pnlSecretBypass.Visibility = (pnlSecretBypass.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void btnOpenRegister_Click(object sender, RoutedEventArgs e) => pnlRegister.Visibility = Visibility.Visible;
        private void btnCloseRegister_Click(object sender, RoutedEventArgs e) => pnlRegister.Visibility = Visibility.Collapsed;

        private void btnOwnerBypass_Click(object sender, RoutedEventArgs e)
        {
            string typedPass = (SplashScreen.Visibility == Visibility.Visible) ? txtSplashBypassPass.Password : txtBypassPass.Password;

            if (_isOwnerBypassActive)
            {
                _isOwnerBypassActive = false;
                SaveBypassEncrypted(false);
                txtBypassPass.Password = "";
                txtSplashBypassPass.Password = "";
                btnOwnerBypass.Content = "DEBLOCHEAZĂ";
                btnOwnerBypass.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                btnOwnerBypass.Foreground = Brushes.White;
                MessageBox.Show("Bypass DEZACTIVAT.", "EVO-CRAFT");
                return;
            }

            if (!string.IsNullOrEmpty(OwnerSecretPass) && typedPass == OwnerSecretPass)
            {
                _isOwnerBypassActive = true;
                SaveBypassEncrypted(true);
                MessageBox.Show("MOD OWNER ACTIVAT! Poți intra chiar dacă e mentenanță.", "EVO-CRAFT BYPASS");

                btnOwnerBypass.Background = Brushes.Gold;
                btnOwnerBypass.Content = "ACTIV (Apasă STOP)";
                btnOwnerBypass.Foreground = Brushes.Black;

                if (SplashScreen.Visibility == Visibility.Visible)
                {
                    pbSplashLoading.Visibility = Visibility.Visible;
                    btnExitVps.Visibility = Visibility.Collapsed;
                    lblSplashLoadingText.Foreground = new SolidColorBrush(Color.FromRgb(67, 181, 129));
                    pnlSecretBypass.Visibility = Visibility.Collapsed;
                    InitLauncherSequence();
                }
            }
            else
            {
                MessageBox.Show("Parolă incorectă!", "EROARE");
            }
        }

        private void CleanFoldersBeforeStart()
        {
            if (_isOwnerBypassActive) return;

            string modsPath = Path.Combine(_userDataPath, "mods");
            if (Directory.Exists(modsPath))
            {
                foreach (var mod in Directory.GetFiles(modsPath, "*.jar"))
                {
                    if (!officialMods.Contains(Path.GetFileName(mod)) && Path.GetFileName(mod) != VoiceChatModFileName)
                    {
                        try { File.Delete(mod); MessageBox.Show($"Vezi că ai băgat un mod diferit ({Path.GetFileName(mod)})! Nu este permis!", "SECURITATE"); } catch { }
                    }
                }
            }

            string[] foldersToClean = { "resourcepacks", "shaderpacks" };
            foreach (var folder in foldersToClean)
            {
                string path = Path.Combine(_userDataPath, folder);
                if (!Directory.Exists(path)) continue;
                foreach (var f in Directory.GetFiles(path))
                    if (f.ToLower().Contains("xray")) try { File.Delete(f); MessageBox.Show($"XRAY detectat în {folder} și șters!", "SECURITATE"); } catch { }
                foreach (var d in Directory.GetDirectories(path))
                    if (d.ToLower().Contains("xray")) try { Directory.Delete(d, true); MessageBox.Show($"Folder XRAY detectat în {folder} și șters!", "SECURITATE"); } catch { }
            }
        }

        private void UpdateDatabaseUI(bool loggedIn)
        {
            Dispatcher.Invoke(() => {
                if (loggedIn)
                {
                    pnlUsernameInput.Visibility = Visibility.Collapsed;
                    pnlDbLoggedInMsg.Visibility = Visibility.Visible;
                    lblDbLoggedUser.Text = $"Ești logat ca: {txtUsername.Text}";
                }
                else
                {
                    pnlUsernameInput.Visibility = Visibility.Visible;
                    pnlDbLoggedInMsg.Visibility = Visibility.Collapsed;
                }
                UpdateLaunchButtonState();
            });
        }

        private async void btnShowWebToken_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null)
            {
                MessageBox.Show("Trebuie să fii logat în launcher pentru a genera un cod de login browser!", "SISTEM");
                return;
            }

            if (txtWebToken.Text != "********")
            {
                txtWebToken.Text = "********";
                btnShowWebToken.Content = "AFIȘEAZĂ COD";
                return;
            }

            try
            {
                btnShowWebToken.IsEnabled = false;
                btnShowWebToken.Content = "GENERARE...";

                using (HttpClient client = new HttpClient())
                {
                    var payload = new { username = _session.Username, auth_key = ApiAuthKey };
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{VpsApiUrl}/generate-web-token", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var jobj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(jsonResponse);
                        var tokenVal = jobj?.Value<string?>("token");
                        if (!string.IsNullOrEmpty(tokenVal))
                        {
                            _currentWebToken = tokenVal;
                            txtWebToken.Text = _currentWebToken;
                            btnShowWebToken.Content = "ASCUNDE COD";
                        }
                    }
                    else
                    {
                        MessageBox.Show("Eroare VPS: Nu s-a putut genera token-ul.");
                        txtWebToken.Text = "********";
                        btnShowWebToken.Content = "AFIȘEAZĂ COD";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Eroare conexiune: " + ex.Message);
            }
            finally
            {
                btnShowWebToken.IsEnabled = true;
            }
        }

        private void btnOpenWebLogin_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentWebToken) || txtWebToken.Text == "********")
            {
                MessageBox.Show("Generează întâi un cod prin butonul de mai sus!", "INFO");
                return;
            }

            string url = $"http://evocraft.ro/login.php?token={_currentWebToken}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private async Task PerformMicrosoftLogin(bool silent = false)
        {
            try
            {
                var handler = new JELoginHandlerBuilder().WithAccountManager(Path.Combine(_userDataPath, "login_cache")).Build();
                _premiumSession = silent ? await handler.AuthenticateSilently() : await handler.AuthenticateInteractively();

                if (_premiumSession != null)
                {
                    string syncResult = await SyncPremiumUserWithDb(_premiumSession.Username, _premiumSession.UUID);

                    if (syncResult == "BANNED")
                    {
                        UpdateLaunchButtonState();
                        MessageBox.Show("Acest calculator sau cont a fost BANAT permanent de pe EVO CRAFT!", "INTERZIS", MessageBoxButton.OK, MessageBoxImage.Error);
                        UpdateCrackedUI();
                        return;
                    }
                    else if (syncResult == "NEEDS_REFERRAL")
                    {
                        _pendingUsername = _premiumSession.Username;
                        _pendingPassword = "";
                        _pendingIsPremium = true;
                        Dispatcher.Invoke(() => {
                            SplashScreen.Visibility = Visibility.Collapsed;
                            var pnl = FindName("pnlReferralPrompt") as Grid;
                            if (pnl != null) pnl.Visibility = Visibility.Visible;
                        });
                        return;
                    }

                    _pendingUsername = _premiumSession.Username;
                    _pendingPassword = "";
                    _pendingIsPremium = true;

                    FinalizeLoginAndSetup();
                }
            }
            catch { UpdateCrackedUI(); }
        }

        private void UpdateCrackedUI()
        {
            _isPremiumMode = false;
            _isDatabaseLoggedIn = false;
            btnTabCracked.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
            btnTabPremium.Background = Brushes.Transparent;
            UpdateDatabaseUI(false);
            pnlPremiumMsg.Visibility = Visibility.Collapsed;
            lblAccountType.Text = "Cont Launcher";
            SaveLoginMode("cracked");
        }

        private void UpdatePremiumUI()
        {
            _isPremiumMode = true;
            btnTabPremium.Background = new SolidColorBrush(Color.FromRgb(67, 181, 129));
            btnTabCracked.Background = Brushes.Transparent;
            pnlUsernameInput.Visibility = Visibility.Collapsed;
            pnlDbLoggedInMsg.Visibility = Visibility.Collapsed;
            pnlPremiumMsg.Visibility = Visibility.Visible;
            lblAccountType.Text = "Cont Premium";
            SaveLoginMode("premium");
        }

        private void SaveLoginMode(string mode) { EncryptAndSaveFile("login_mode.dat", mode); }

        private void btnTabCracked_Click(object sender, RoutedEventArgs e) => UpdateCrackedUI();
        private async void btnTabPremium_Click(object sender, RoutedEventArgs e)
        {
            if (_premiumSession != null)
            {
                UpdatePremiumUI();
                await ShowProfile(_premiumSession.Username);
                UpdateLaunchButtonState();
                return;
            }
            await PerformMicrosoftLogin(File.Exists(Path.Combine(_userDataPath, "login_cache")));
        }

        private async void txtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPremiumMode) await ShowProfile(txtUsername.Text);
        }

        private async Task ShowProfile(string? u)
        {
            string user = string.IsNullOrEmpty(u) ? "MHF_Steve" : u;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync($"https://minotar.net/helm/{user}/100.png");
                    var img = new BitmapImage();
                    using (var ms = new MemoryStream(data))
                    {
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                    }
                    img.Freeze(); // INGHEATA IMAGINEA - OPRESTE CONSUMUL DE RAM EXTRA WPF
                    imgSkin.ImageSource = img;
                    lblProfileName.Text = user;
                }
            }
            catch { }
            finally { FlushMemory(); }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                Dispatcher.Invoke(() => {
                    if (lblModpackVersion != null)
                    {
                        string ver = "1.0";
                        if (_selectedGameMode == "Survival") ver = OnlineModpackVersionSurvival;
                        else if (_selectedGameMode == "Skyblock") ver = OnlineModpackVersionSkyblock;
                        else if (_selectedGameMode == "Creative") ver = OnlineModpackVersionCreative;
                        else if (_selectedGameMode == "Parkour") ver = OnlineModpackVersionParkour;

                        lblModpackVersion.Text = $"MODPACK: v{ver} ({_selectedGameMode})";
                    }
                });
            }
            catch { }
        }

        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Deconectare?", "Log Out", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _premiumSession = null;
                _session = null;
                if (File.Exists(Path.Combine(_userDataPath, "login_cache"))) File.Delete(Path.Combine(_userDataPath, "login_cache"));
                _isDatabaseLoggedIn = false;
                if (File.Exists(Path.Combine(_userDataPath, "db_session.dat"))) File.Delete(Path.Combine(_userDataPath, "db_session.dat"));
                UpdateCrackedUI(); _ = ShowProfile("MHF_Steve"); UpdateLaunchButtonState();
                _totalPlayTime = TimeSpan.Zero;
                UpdatePlaytimeUI();
                FlushMemory();
            }
        }

        private void btnDeleteData_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Ștergi modpack-ul?", "Resetare", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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
                catch { }
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void btnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void btnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void btnSettings_Click(object sender, RoutedEventArgs e) => pnlSettings.Visibility = Visibility.Visible;
        private void btnCloseSettings_Click(object sender, RoutedEventArgs e) => pnlSettings.Visibility = Visibility.Collapsed;
        private void bgVideo_Loaded(object sender, RoutedEventArgs e) => bgVideo.Play();
        private void bgVideo_MediaEnded(object sender, RoutedEventArgs e) { bgVideo.Position = TimeSpan.FromMilliseconds(1); bgVideo.Play(); }
        private void OpenTikTok(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://tiktok.com/@evocraftro") { UseShellExecute = true });
        private void OpenYouTube(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://youtube.com/@EvoCraftRo") { UseShellExecute = true });
        private void OpenDiscord(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://discord.gg/e9yHMaMwTQ") { UseShellExecute = true });

        private void UpdatePlaytimeUI()
        {
            try
            {
                Dispatcher.Invoke(() => {
                    if (lblPlayTime != null)
                    {
                        lblPlayTime.Visibility = Visibility.Visible;
                        int totalHours = (int)_totalPlayTime.TotalHours;
                        int minutes = _totalPlayTime.Minutes;
                        lblPlayTime.Text = $"ORE JUCATE: {totalHours}h {minutes}m";
                    }
                });
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            StopHttpServer();
            discordClient?.Dispose();
            base.OnClosing(e);
        }
    }
}