using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Lunex.Services
{
    public class UpdateInfo
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("release_notes")]
        public string? ReleaseNotes { get; set; }

        [JsonPropertyName("bug_fixes")]
        public string? BugFixes { get; set; }
    }

    public class UpdateService : INotifyPropertyChanged
    {
        // Supabase credentials - do not commit admin keys here you absolute donuts
        private const string SupabaseUrl = "https://qhihazzhrtiiwphtinpj.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InFoaWhhenpocnRpaXdwaHRpbnBqIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODIwOTMxOTEsImV4cCI6MjA5NzY2OTE5MX0.xgNLwwsZG7U6hevffRXfnHkYsufDrcrS9VUuANRtoYw";

        private const string TableEndpoint = "/rest/v1/app_updates?select=*&order=id.desc&limit=1";

        public const string CurrentVersion = "7.5.11";

        // Single instance of updater so we don't spam database connections
        private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        // Custom HttpClient wrapper with fallback DNS-over-HTTPS
        private static readonly HttpClient _http = new(new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                // DO NOT FUCKING TOUCH THIS OR DOH RESOLVER BYPASS BREAKS!
                // Some users' ISPs block Supabase domains, so if standard DNS lookup fails, we fall back to Google DoH (8.8.8.8)
                if (host.Equals("qhihazzhrtiiwphtinpj.supabase.co", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var ips = await System.Net.Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                        if (ips.Length > 0)
                        {
                            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                            await socket.ConnectAsync(new System.Net.IPEndPoint(ips[0], port), cancellationToken).ConfigureAwait(false);
                            return new System.Net.Sockets.NetworkStream(socket, true);
                        }
                    }
                    catch
                    {
                        // Standard DNS failed - resolve via DoH now
                        var ip = await ResolveViaDoHAsync(host, cancellationToken).ConfigureAwait(false);
                        if (ip != null)
                        {
                            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                            await socket.ConnectAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), port), cancellationToken).ConfigureAwait(false);
                            return new System.Net.Sockets.NetworkStream(socket, true);
                        }
                        throw;
                    }
                }

                var defaultSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                await defaultSocket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(defaultSocket, true);
            }
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // HttpClient dedicated specifically to DoH queries to prevent reentrancy deadlocks on the custom SocketsHttpHandler
        private static readonly HttpClient _dohHttp = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static async Task<string?> ResolveViaDoHAsync(string host, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://8.8.8.8/resolve?name={Uri.EscapeDataString(host)}&type=A");
                using var response = await _dohHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Answer", out var answerProp) && answerProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in answerProp.EnumerateArray())
                        {
                            if (item.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
                            {
                                return dataProp.GetString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] DoH resolution failed: {ex.Message}");
            }
            return null;
        }

        // Internal state variables - UI bindings listen to these
        private bool _isCheckingForUpdate;
        private bool _isDownloading;
        private double _downloadProgress;
        private bool _updateAvailable;
        private bool _updateDownloaded;
        private string _latestVersion = string.Empty;
        private string _downloadUrl = string.Empty;
        private string _statusText = string.Empty;
        private string? _downloadedInstallerPath;
        private CancellationTokenSource? _downloadCts;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? ForceUpdateRequired;

        static UpdateService()
        {
            try
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 LUNEX-UPDATER/1.0");
            }
            catch { }
        }

        private UpdateService() { }

        public void CancelDownload()
        {
            try
            {
                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
            catch { }
        }

        // ── Public properties (bindable) ─────────────────────────────────────
        public bool IsCheckingForUpdate
        {
            get => _isCheckingForUpdate;
            private set => SetField(ref _isCheckingForUpdate, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set => SetField(ref _isDownloading, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            private set => SetField(ref _downloadProgress, value);
        }

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            private set => SetField(ref _updateAvailable, value);
        }

        public bool UpdateDownloaded
        {
            get => _updateDownloaded;
            private set => SetField(ref _updateDownloaded, value);
        }

        public string LatestVersion
        {
            get => _latestVersion;
            private set => SetField(ref _latestVersion, value);
        }

        public string DownloadUrl
        {
            get => _downloadUrl;
            private set => SetField(ref _downloadUrl, value);
        }

        // UI status text
        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        // Background updater run silently on launch (fire-and-forget)
        public async Task CheckAndDownloadOnLaunchAsync()
        {
            try
            {
                var info = await FetchLatestUpdateInfoAsync().ConfigureAwait(false);
                if (info == null) return;

                LatestVersion = info.Version;
                DownloadUrl = info.DownloadUrl;

                var tempDir = Path.Combine(Path.GetTempPath(), "LunexUpdates");
                var fileName = Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "LunexSetup.exe";
                var destPath = Path.Combine(tempDir, fileName);

                // clean stale temp file from aborted download
                try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }

                PruneOldUpdates(fileName);

                if (IsNewerVersion(info.Version, CurrentVersion))
                {
                    UpdateAvailable = true;

                    if (await IsInstallerValidAsync(destPath, info.Version, info.DownloadUrl).ConfigureAwait(false))
                    {
                        _downloadedInstallerPath = destPath;
                        UpdateDownloaded = true;
                        ForceUpdateRequired?.Invoke();
                    }
                    else
                    {
                        // Clean up invalid or partial setup file if it exists
                        try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                        UpdateDownloaded = false;

                        // Silent background download
                        await DownloadUpdateAsync(info.DownloadUrl).ConfigureAwait(false);
                    }
                }
                else
                {
                    UpdateAvailable = false;
                    UpdateDownloaded = false;

                    // Clean up any old installer files if the app is already up to date
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] On-launch check failed: {ex.Message}");
            }
        }

        public async Task<bool> CheckForUpdateManuallyAsync()
        {
            if (IsCheckingForUpdate || IsDownloading) return false;

            try
            {
                IsCheckingForUpdate = true;
                StatusText = "Checking for updates...";
                UpdateAvailable = false;

                var info = await FetchLatestUpdateInfoAsync().ConfigureAwait(false);

                if (info == null)
                {
                    StatusText = "Could not reach update server.";
                    return false;
                }

                LatestVersion = info.Version;
                DownloadUrl = info.DownloadUrl;

                var tempDir = Path.Combine(Path.GetTempPath(), "LunexUpdates");
                var fileName = Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "LunexSetup.exe";
                var destPath = Path.Combine(tempDir, fileName);

                // clean stale temp file from aborted download
                try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }

                PruneOldUpdates(fileName);

                if (IsNewerVersion(info.Version, CurrentVersion))
                {
                    UpdateAvailable = true;

                    if (await IsInstallerValidAsync(destPath, info.Version, info.DownloadUrl).ConfigureAwait(false))
                    {
                        _downloadedInstallerPath = destPath;
                        UpdateDownloaded = true;
                        StatusText = "Update ready — click to install.";
                    }
                    else
                    {
                        try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                        UpdateDownloaded = false;
                        StatusText = $"Update available: v{info.Version}";
                        _ = Task.Run(() => DownloadUpdateAsync(info.DownloadUrl));
                    }
                    return false;
                }
                else
                {
                    UpdateAvailable = false;
                    UpdateDownloaded = false;
                    StatusText = "App is up to date.";
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                    return true; // already on latest
                }
            }
            catch (Exception ex)
            {
                if (ex is System.Net.Sockets.SocketException || ex is HttpRequestException || ex.Message.Contains("host is known"))
                {
                    StatusText = "Check failed: No internet connection.";
                }
                else
                {
                    StatusText = $"Check failed: {ex.Message}";
                }
                Console.WriteLine($"[UpdateService] Manual check failed: {ex.Message}");
                return false;
            }
            finally
            {
                IsCheckingForUpdate = false;
            }
        }

        // run the setup.exe file we just downloaded
        public void LaunchInstaller()
        {
            if (!UpdateDownloaded || string.IsNullOrEmpty(_downloadedInstallerPath)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _downloadedInstallerPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Failed to launch installer: {ex.Message}");
            }
        }

        // internal helpers

        private async Task<UpdateInfo?> FetchLatestUpdateInfoAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SupabaseUrl + TableEndpoint);
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Headers.Add("Authorization", $"Bearer {SupabaseAnonKey}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[UpdateService] HTTP {(int)response.StatusCode} fetching update info.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<UpdateInfo[]>(json);
            return list != null && list.Length > 0 ? list[0] : null;
        }

        private async Task DownloadUpdateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            var tempDir = Path.Combine(Path.GetTempPath(), "LunexUpdates");
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "LunexSetup.exe";
            var destPath = Path.Combine(tempDir, fileName);
            var tempFilePath = destPath + ".tmp";

            // delete old temp file from previuos download if it's still lying around
            try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }

            try
            {
                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
            }
            catch { }
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                IsDownloading = true;
                DownloadProgress = 0;
                StatusText = "Downloading update...";

                Directory.CreateDirectory(tempDir);
                PruneOldUpdates(fileName);

                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var buffer = new byte[81920];
                long downloaded = 0;

                await using (var contentStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                        downloaded += bytesRead;
                        if (totalBytes > 0)
                        {
                            DownloadProgress = (double)downloaded / totalBytes * 100.0;
                            StatusText = $"Downloading... {DownloadProgress:F0}%";
                        }
                    }
                }

                token.ThrowIfCancellationRequested();

                if (totalBytes > 0 && downloaded != totalBytes)
                {
                    throw new IOException($"Download was incomplete. Expected {totalBytes} bytes but got {downloaded} bytes.");
                }

                // delete old setup file so we don't get file lock issues
                try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }

                // rename the temp file to the final destination path
                File.Move(tempFilePath, destPath);

                _downloadedInstallerPath = destPath;
                UpdateDownloaded = true;
                DownloadProgress = 100;
                StatusText = "Update ready — click to install.";
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                {
                    StatusText = "Download cancelled.";
                }
                else
                {
                    StatusText = $"Download failed: {ex.Message}";
                    Console.WriteLine($"[UpdateService] Download failed: {ex.Message}");
                }
                UpdateDownloaded = false;

                // garbage cleanup of partial files
                try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void PruneOldUpdates(string latestFileName)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "LunexUpdates");
                if (Directory.Exists(tempDir))
                {
                    foreach (var file in Directory.GetFiles(tempDir))
                    {
                        try
                        {
                            var name = Path.GetFileName(file);
                            if (!name.Equals(latestFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[UpdateService] Error deleting old update file '{file}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Error pruning old updates: {ex.Message}");
            }
        }

        // DO NOT FUCKING CHANGE THIS. Parses executable headers manually to check if it is a valid PE format. If this returns false, the installer won't run.
        private static bool IsValidPeFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 64) return false;

                var buffer = new byte[4];
                
                // check MZ magic byte
                if (fs.Read(buffer, 0, 2) != 2) return false;
                if (buffer[0] != 0x4D || buffer[1] != 0x5A) return false;

                // get offset to PE header at 0x3C offset
                fs.Position = 0x3C;
                if (fs.Read(buffer, 0, 4) != 4) return false;
                var peOffset = BitConverter.ToInt32(buffer, 0);

                if (peOffset < 0 || peOffset + 4 > fs.Length) return false;

                // check PE sign
                fs.Position = peOffset;
                if (fs.Read(buffer, 0, 4) != 4) return false;
                return buffer[0] == 0x50 && buffer[1] == 0x45 && buffer[2] == 0x00 && buffer[3] == 0x00;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsInstallerValidAsync(string filePath, string targetVersion, string downloadUrl)
        {
            if (!File.Exists(filePath)) return false;

            // 1. check PE headers aren't corrupted
            if (!IsValidPeFile(filePath))
            {
                Console.WriteLine($"[UpdateService] Installer PE validation failed: '{filePath}'");
                return false;
            }

            // 2. check installer matches target version
            if (!IsInstallerForVersion(filePath, targetVersion))
            {
                return false;
            }

            // 3. check file size is correct
            var localSize = new FileInfo(filePath).Length;
            if (localSize <= 0) return false;

            var remoteSize = await GetRemoteFileSizeAsync(downloadUrl).ConfigureAwait(false);
            if (remoteSize > 0 && localSize != remoteSize)
            {
                Console.WriteLine($"[UpdateService] Installer size mismatch. Local: {localSize}, Remote: {remoteSize}");
                return false;
            }

            return true;
        }

        private bool IsInstallerForVersion(string filePath, string targetVersion)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                var prodVersion = versionInfo.ProductVersion?.Trim();
                var fileVersion = versionInfo.FileVersion?.Trim();

                if (string.IsNullOrEmpty(prodVersion) && string.IsNullOrEmpty(fileVersion))
                {
                    return false;
                }

                var cleanTarget = CleanVersionString(targetVersion);

                if (!string.IsNullOrEmpty(prodVersion))
                {
                    var cleanProd = CleanVersionString(prodVersion);
                    if (cleanProd == cleanTarget) return true;
                    if (Version.TryParse(cleanProd, out var parsedProd) && Version.TryParse(cleanTarget, out var parsedTarget))
                    {
                        if (parsedProd == parsedTarget) return true;
                    }
                }

                if (!string.IsNullOrEmpty(fileVersion))
                {
                    var cleanFile = CleanVersionString(fileVersion);
                    if (cleanFile == cleanTarget) return true;
                    if (Version.TryParse(cleanFile, out var parsedFile) && Version.TryParse(cleanTarget, out var parsedTarget2))
                    {
                        if (parsedFile == parsedTarget2) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Error reading installer version: {ex.Message}");
            }
            return false;
        }

        private async Task<long> GetRemoteFileSizeAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return -1L;
            try
            {
                // Try HEAD request first
                using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResponse = await _http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength.HasValue)
                {
                    return headResponse.Content.Headers.ContentLength.Value;
                }

                // Fallback to GET headers read only
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResponse = await _http.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (getResponse.IsSuccessStatusCode && getResponse.Content.Headers.ContentLength.HasValue)
                {
                    return getResponse.Content.Headers.ContentLength.Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Error getting remote file size: {ex.Message}");
            }
            return -1L;
        }

        public static string? GetPendingUpdateInstaller()
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "LunexUpdates");
                if (!Directory.Exists(tempDir)) return null;

                foreach (var file in Directory.GetFiles(tempDir, "*.exe"))
                {
                    if (IsValidPeFile(file))
                    {
                        var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(file);
                        var fileVersion = versionInfo.FileVersion?.Trim();
                        var prodVersion = versionInfo.ProductVersion?.Trim();
                        
                        string? versionToCompare = null;
                        if (!string.IsNullOrEmpty(prodVersion)) versionToCompare = prodVersion;
                        else if (!string.IsNullOrEmpty(fileVersion)) versionToCompare = fileVersion;

                        if (versionToCompare != null)
                        {
                            var cleanVersion = CleanVersionString(versionToCompare);
                            if (IsNewerVersion(cleanVersion, CurrentVersion))
                            {
                                return file;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Error checking for pending update: {ex.Message}");
            }
            return null;
        }

        private static string CleanVersionString(string version)
        {
            version = version.Trim();
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Substring(1);
            }

            var nullCharIndex = version.IndexOf('\0');
            if (nullCharIndex >= 0)
            {
                version = version.Substring(0, nullCharIndex);
            }

            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                version = version.Substring(0, dashIndex);
            }

            return version.Trim();
        }

        /// <summary>Returns true if <paramref name="latest"/> is a strictly higher version than <paramref name="current"/>.</summary>
        private static bool IsNewerVersion(string latest, string current)
        {
            if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
                return l > c;
            return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            // WPF bindings require PropertyChanged on the UI thread
            var app = System.Windows.Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(() => handler(this, new PropertyChangedEventArgs(name)));
            else
                handler(this, new PropertyChangedEventArgs(name));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
