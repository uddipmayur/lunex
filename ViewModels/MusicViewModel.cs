using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public enum MusicSource { Local, Spotify, YtMusic, Yt }

    public class MusicViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainVm;
        private readonly string _songsDir;
        private readonly MediaPlayer _mediaPlayer;
        private readonly DispatcherTimer _playbackTimer;
        private bool _isUpdatingFromTimer;

        private MusicSource _activeSource = MusicSource.Local;
        public MusicSource ActiveSource
        {
            get => _activeSource;
            set
            {
                if (SetProperty(ref _activeSource, value))
                {
                    OnPropertyChanged(nameof(IsLocalActive));
                    OnPropertyChanged(nameof(IsWebActive));
                    OnPropertyChanged(nameof(WebPortalUrl));
                    OnPropertyChanged(nameof(WebPortalName));

                    // Pause local playback when switching to web sources
                    if (_activeSource != MusicSource.Local)
                        IsPlaying = false;
                }
            }
        }

        public bool IsLocalActive => ActiveSource == MusicSource.Local;
        public bool IsWebActive => ActiveSource != MusicSource.Local;

        public string WebPortalUrl => ActiveSource switch
        {
            MusicSource.Spotify => "https://open.spotify.com",
            MusicSource.YtMusic => "https://music.youtube.com",
            MusicSource.Yt => "https://www.youtube.com",
            _ => string.Empty
        };

        public string WebPortalName => ActiveSource switch
        {
            MusicSource.Spotify => "Spotify",
            MusicSource.YtMusic => "YouTube Music",
            MusicSource.Yt => "YouTube",
            _ => string.Empty
        };

        // Local Audio State
        public ObservableCollection<string> LocalSongs { get; } = new();

        private string _activeTrackTitle = "System Dormant";
        public string ActiveTrackTitle
        {
            get => _activeTrackTitle;
            set
            {
                if (SetProperty(ref _activeTrackTitle, value))
                {
                    UpdateTrackDetails();
                    LoadAndPlayActiveTrack();
                }
            }
        }

        private string _activeTrackFormat = "Binary Stream";
        public string ActiveTrackFormat
        {
            get => _activeTrackFormat;
            set => SetProperty(ref _activeTrackFormat, value);
        }

        private string _activeTrackSize = "0 KB";
        public string ActiveTrackSize
        {
            get => _activeTrackSize;
            set => SetProperty(ref _activeTrackSize, value);
        }

        private string _activeTrackSpec = "Decoded Virtual Stream";
        public string ActiveTrackSpec
        {
            get => _activeTrackSpec;
            set => SetProperty(ref _activeTrackSpec, value);
        }

        private string _activeTrackLocation = "RAM Virtual Orbit";
        public string ActiveTrackLocation
        {
            get => _activeTrackLocation;
            set => SetProperty(ref _activeTrackLocation, value);
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    if (_isPlaying)
                    {
                        PlayAudio();
                    }
                    else
                    {
                        PauseAudio();
                    }
                }
            }
        }

        private double _volume = 0.75;
        public double Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value))
                {
                    _mediaPlayer.Volume = _volume;
                }
            }
        }

        private double _currentSeconds;
        public double CurrentSeconds
        {
            get => _currentSeconds;
            set
            {
                if (SetProperty(ref _currentSeconds, value))
                {
                    OnPropertyChanged(nameof(FormattedCurrentTime));
                    if (!_isUpdatingFromTimer)
                    {
                        _mediaPlayer.Position = TimeSpan.FromSeconds(_currentSeconds);
                    }
                }
            }
        }

        private double _durationSeconds = 180.0;
        public double DurationSeconds
        {
            get => _durationSeconds;
            set => SetProperty(ref _durationSeconds, value);
        }

        public string FormattedCurrentTime => FormatTime(CurrentSeconds);
        public string FormattedDurationTime => FormatTime(DurationSeconds);

        public ICommand TogglePlayCommand { get; }
        public ICommand NextTrackCommand { get; }
        public ICommand PrevTrackCommand { get; }
        public ICommand RescanCommand { get; }
        public ICommand OpenSongsFolderCommand { get; }
        public ICommand OpenBrowserCommand { get; }

        public MusicViewModel(MainViewModel mainVm)
        {
            _mainVm = mainVm;

            // Resolve local songs path
            var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _songsDir = Path.Combine(docsDir, "Lunex", "songs");
            if (!Directory.Exists(_songsDir))
            {
                Directory.CreateDirectory(_songsDir);
            }

            // Initialize MediaPlayer and Timer
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaOpened += OnMediaOpened;
            _mediaPlayer.MediaEnded += OnMediaEnded;

            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _playbackTimer.Tick += OnPlaybackTimerTick;

            TogglePlayCommand = new RelayCommand(() =>
            {
                if (LocalSongs.Count > 0)
                {
                    if (ActiveTrackTitle == "System Dormant")
                        ActiveTrackTitle = LocalSongs[0]; // setter auto-starts playback
                    else
                        IsPlaying = !IsPlaying;
                }
            });

            NextTrackCommand = new RelayCommand(() =>
            {
                if (LocalSongs.Count > 1)
                {
                    int currentIdx = LocalSongs.IndexOf(ActiveTrackTitle);
                    int nextIdx = (currentIdx + 1) % LocalSongs.Count;
                    ActiveTrackTitle = LocalSongs[nextIdx];
                }
            });

            PrevTrackCommand = new RelayCommand(() =>
            {
                if (LocalSongs.Count > 1)
                {
                    int currentIdx = LocalSongs.IndexOf(ActiveTrackTitle);
                    int prevIdx = (currentIdx - 1 + LocalSongs.Count) % LocalSongs.Count;
                    ActiveTrackTitle = LocalSongs[prevIdx];
                }
            });

            RescanCommand = new RelayCommand(ScanSongsFolder);
            OpenSongsFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{_songsDir}\"");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening songs folder: {ex.Message}");
                }
            });

            OpenBrowserCommand = new RelayCommand(() => OpenWebPortalInDefaultBrowser(WebPortalUrl));

            // Initial scan
            ScanSongsFolder();
            UpdateTrackDetails();
        }

        private void OnMediaOpened(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                DurationSeconds = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                OnPropertyChanged(nameof(FormattedDurationTime));
            }
        }

        private void OnMediaEnded(object? sender, EventArgs e)
        {
            if (NextTrackCommand.CanExecute(null))
            {
                NextTrackCommand.Execute(null);
            }
            else
            {
                IsPlaying = false;
                CurrentSeconds = 0;
            }
        }

        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                double totalSec = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (Math.Abs(DurationSeconds - totalSec) > 0.1)
                {
                    DurationSeconds = totalSec;
                    OnPropertyChanged(nameof(FormattedDurationTime));
                }
            }

            _isUpdatingFromTimer = true;
            CurrentSeconds = _mediaPlayer.Position.TotalSeconds;
            _isUpdatingFromTimer = false;
        }

        private void PlayAudio()
        {
            if (ActiveTrackTitle == "System Dormant" && LocalSongs.Count > 0)
            {
                ActiveTrackTitle = LocalSongs[0];
                return;
            }

            if (!string.IsNullOrEmpty(ActiveTrackTitle) && ActiveTrackTitle != "System Dormant")
            {
                _mediaPlayer.Play();
                _playbackTimer.Start();
            }
        }

        private void PauseAudio()
        {
            _mediaPlayer.Pause();
            _playbackTimer.Stop();
        }

        private void LoadAndPlayActiveTrack()
        {
            if (string.IsNullOrEmpty(ActiveTrackTitle) || ActiveTrackTitle == "System Dormant")
            {
                _mediaPlayer.Close();
                IsPlaying = false;
                CurrentSeconds = 0;
                DurationSeconds = 180.0;
                return;
            }

            var filePath = Path.Combine(_songsDir, ActiveTrackTitle);
            if (File.Exists(filePath))
            {
                try
                {
                    _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
                    _mediaPlayer.Volume = Volume;
                    if (IsPlaying)
                    {
                        _mediaPlayer.Play();
                        _playbackTimer.Start();
                    }
                    else
                    {
                        _playbackTimer.Stop();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error playing file: {ex.Message}");
                }
            }
        }

        private void ScanSongsFolder()
        {
            try
            {
                LocalSongs.Clear();
                if (Directory.Exists(_songsDir))
                {
                    var extensions = new[] { ".mp3", ".wav", ".m4a", ".ogg", ".flac" };
                    var files = Directory.GetFiles(_songsDir)
                                         .Where(file => extensions.Contains(Path.GetExtension(file).ToLower()))
                                         .Select(Path.GetFileName)
                                         .ToList();

                    foreach (var file in files)
                    {
                        if (file != null) LocalSongs.Add(file);
                    }

                    if (LocalSongs.Count > 0 && ActiveTrackTitle == "System Dormant")
                    {
                        ActiveTrackTitle = LocalSongs[0];
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning audio files: {ex.Message}");
            }
        }

        private void UpdateTrackDetails()
        {
            try
            {
                if (ActiveTrackTitle == "System Dormant" || string.IsNullOrEmpty(ActiveTrackTitle))
                {
                    ActiveTrackFormat = "Binary Stream";
                    ActiveTrackSize = "0 KB";
                    ActiveTrackSpec = "Decoded Virtual Stream";
                    ActiveTrackLocation = "RAM Virtual Orbit";
                    return;
                }

                var filePath = Path.Combine(_songsDir, ActiveTrackTitle);
                if (File.Exists(filePath))
                {
                    var fi = new FileInfo(filePath);
                    double kbSize = fi.Length / 1024.0;
                    if (kbSize >= 1024.0)
                    {
                        ActiveTrackSize = $"{kbSize / 1024.0:F2} MB";
                    }
                    else
                    {
                        ActiveTrackSize = $"{kbSize:F1} KB";
                    }

                    var ext = Path.GetExtension(filePath).ToLower();
                    ActiveTrackFormat = ext switch
                    {
                        ".wav" => "Waveform Audio File (WAV)",
                        ".mp3" => "MPEG-1 Audio Layer 3 (MP3)",
                        ".flac" => "Free Lossless Audio Codec (FLAC)",
                        ".m4a" => "MPEG-4 Audio (M4A)",
                        ".ogg" => "Ogg Vorbis (OGG)",
                        _ => ext.ToUpper().TrimStart('.') + " Audio"
                    };

                    ActiveTrackLocation = Path.GetDirectoryName(filePath) ?? _songsDir;

                    // Read WAV spec if WAV
                    if (ext == ".wav")
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (fs.Length >= 44)
                            {
                                byte[] header = new byte[44];
                                fs.ReadExactly(header, 0, 44);
                                if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
                                {
                                    int channels = BitConverter.ToInt16(header, 22);
                                    int sampleRate = BitConverter.ToInt32(header, 24);
                                    int bitsPerSample = BitConverter.ToInt16(header, 34);
                                    string channelStr = channels == 2 ? "Stereo" : (channels == 1 ? "Mono" : $"{channels} Ch");
                                    ActiveTrackSpec = $"{channelStr} · {sampleRate / 1000.0:F1} kHz · {bitsPerSample}-bit PCM";
                                    return;
                                }
                            }
                        }
                    }

                    // Fallback spec for MP3 / others
                    ActiveTrackSpec = "Stereo · 44.1 kHz · 320kbps Standard";
                }
                else
                {
                    ActiveTrackFormat = "Unknown";
                    ActiveTrackSize = "0 KB";
                    ActiveTrackSpec = "Offline / Missing";
                    ActiveTrackLocation = "N/A";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating track details: {ex.Message}");
                ActiveTrackFormat = "Unknown";
                ActiveTrackSize = "Error";
                ActiveTrackSpec = "Parsing Error";
                ActiveTrackLocation = "N/A";
            }
        }

        public void OpenWebPortalInDefaultBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching default browser: {ex.Message}");
            }
        }

        private string FormatTime(double seconds)
        {
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}:{secs:D2}";
        }
    }
}
