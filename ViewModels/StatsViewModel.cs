using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public class StatsViewModel : ViewModelBase, IDisposable
    {
        private readonly LibraryService _libraryService;
        private readonly DispatcherTimer _timer;
        private static readonly Random _rand = Random.Shared;

        public string TotalPlaytime { get; }
        public string IntegratedGamesCount { get; }
        public string MostPlayedGameTitle { get; }

        private double _cpuLoad = 24.5;
        public double CpuLoad
        {
            get => _cpuLoad;
            set => SetProperty(ref _cpuLoad, value);
        }

        private double _ramLoad = 48.2;
        public double RamLoad
        {
            get => _ramLoad;
            set => SetProperty(ref _ramLoad, value);
        }

        private int _latency = 22;
        public int Latency
        {
            get => _latency;
            set => SetProperty(ref _latency, value);
        }

        // Active Drive Storage Details
        private string _diskDriveName = "C:\\";
        public string DiskDriveName
        {
            get => _diskDriveName;
            set => SetProperty(ref _diskDriveName, value);
        }

        private double _diskUsagePercentage = 75.0;
        public double DiskUsagePercentage
        {
            get => _diskUsagePercentage;
            set => SetProperty(ref _diskUsagePercentage, value);
        }

        private string _diskSpaceText = "0 GB free of 0 GB";
        public string DiskSpaceText
        {
            get => _diskSpaceText;
            set => SetProperty(ref _diskSpaceText, value);
        }

        public ObservableCollection<double> LatencySparkline { get; } = new();

        public StatsViewModel(MainViewModel mainVm)
        {
            _libraryService = LibraryService.Instance;
            var games = _libraryService.LoadGames();

            // 1. Calculate active statistics
            IntegratedGamesCount = $"{games.Count} Games";
            int totalMinutes = games.Sum(g => g.PlayTimeMinutes);
            TotalPlaytime = $"{totalMinutes / 60.0:F1} hrs";

            var topGame = games.OrderByDescending(g => g.PlayTimeMinutes).FirstOrDefault();
            MostPlayedGameTitle = (topGame != null && topGame.PlayTimeMinutes > 0) ? topGame.Title : "None";

            // 2. Fetch real local storage disk details
            QueryDiskStorage();

            // 3. Initialize simulated live sparkline values
            for (int i = 0; i < 15; i++)
            {
                LatencySparkline.Add(15 + _rand.NextDouble() * 30);
            }

            // 4. Setup lightweight core timer for fluctuations
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _timer.Tick += (s, e) =>
            {
                CpuLoad = 15.0 + _rand.NextDouble() * 25.0;
                RamLoad = 45.0 + _rand.NextDouble() * 5.0;
                Latency = 18 + _rand.Next(10);

                LatencySparkline.RemoveAt(0);
                LatencySparkline.Add(Latency * 1.5);
            };
            _timer.Start();
        }

        private void QueryDiskStorage()
        {
            try
            {
                var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C"));
                if (systemDrive != null)
                {
                    DiskDriveName = $"System Drive ({systemDrive.Name})";
                    double totalGb = systemDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = systemDrive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGb = totalGb - freeGb;

                    DiskUsagePercentage = (usedGb / totalGb) * 100.0;
                    DiskSpaceText = $"{freeGb:F1} GB free of {totalGb:F1} GB";
                }
            }
            catch
            {
                DiskDriveName = "Primary Storage";
                DiskSpaceText = "Disk information unavailable";
            }
        }

        public void StopTimer() => _timer.Stop();

        public void Dispose() => _timer.Stop();
    }
}
