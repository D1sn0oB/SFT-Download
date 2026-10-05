using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SFTLauncher.Download;
using SFTLauncher.Models;

namespace SFTLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        private GameDownloadService? _downloadService;
        private CancellationTokenSource? _currentCts;
        private bool isDownloading = false;
        private List<DownloadRecord> downloadHistory = new List<DownloadRecord>();
        private bool isFirstDownload = true;
        private List<MinecraftVersion> allVersions = new List<MinecraftVersion>();

        public VersionDownloadPage()
        {
            InitializeComponent();
            LoadSettings();
            InitializeDownloadService();
            try
            {
                LoadVersionsFromApi();
                InstallPathBox.Text = GetDefaultMinecraftDirectory();
                PathStatus.Text = "使用默认路径";
                PathStatus.Foreground = (Brush)FindResource("SuccessBrush");
            }
            catch (Exception ex)
            {
                StatusText.Text = "初始化失败: " + ex.Message;
                StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            }
        }

        private void LoadSettings()
        {
            DownloadSettings.ApplySavedSettings();
        }

        private void InitializeDownloadService()
        {
            _downloadService = new GameDownloadService();
            _downloadService.Changed += OnDownloadStateChanged;
        }

        private async void LoadVersionsFromApi()
        {
            try
            {
                UpdateStatus("正在加载版本列表...", "#A78BFA");
                
                var versions = await ManifestGet.GetVersionsAsync();
                allVersions.Clear();
                
                foreach (var version in versions)
                {
                    if (version.Type == "release")
                    {
                        allVersions.Add(version);
                    }
                }
                
                // 只显示最近的版本
                if (allVersions.Count > 50)
                {
                    allVersions = allVersions.GetRange(0, 50);
                }
                
                VersionComboBox.ItemsSource = allVersions;
                VersionComboBox.DisplayMemberPath = "DisplayName";
                if (VersionComboBox.Items.Count > 0)
                    VersionComboBox.SelectedIndex = 0;
                
                VersionStatus.Text = "✓";
                VersionStatus.Foreground = (Brush)FindResource("SuccessBrush");
                UpdateStatus("就绪", "#34D399");
            }
            catch (Exception ex)
            {
                VersionStatus.Text = "✗";
                VersionStatus.Foreground = (Brush)FindResource("ErrorBrush");
                UpdateStatus("加载版本失败: " + ex.Message, "#F87171");
            }
        }

        private string GetDefaultMinecraftDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".minecraft");
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Title = "选择 Minecraft 安装目录";
                dialog.Filter = "文件夹|*.*";
                
                if (dialog.ShowDialog() == true)
                {
                    string selectedPath = Path.GetDirectoryName(dialog.FileName);
                    if (string.IsNullOrEmpty(selectedPath))
                        selectedPath = dialog.FileName;
                    InstallPathBox.Text = selectedPath;
                    CheckMinecraftDirectory();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("错误: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckMinecraftDirectory()
        {
            try
            {
                string mcDir = InstallPathBox.Text;
                if (!string.IsNullOrEmpty(mcDir) && Directory.Exists(mcDir))
                {
                    PathStatus.Text = "目录已存在";
                    PathStatus.Foreground = (Brush)FindResource("SuccessBrush");
                }
                else if (!string.IsNullOrEmpty(mcDir))
                {
                    PathStatus.Text = "将自动创建";
                    PathStatus.Foreground = (Brush)FindResource("WarningBrush");
                }
                else
                {
                    PathStatus.Text = "使用默认路径";
                    PathStatus.Foreground = (Brush)FindResource("TextMutedBrush");
                }
            }
            catch { }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var minecraftDir = string.IsNullOrEmpty(InstallPathBox.Text) 
                ? GetDefaultMinecraftDirectory() 
                : InstallPathBox.Text;

            if (isDownloading) return;
            
            var selectedVersion = VersionComboBox.SelectedItem as MinecraftVersion;
            if (selectedVersion == null)
            {
                MessageBox.Show("请先选择一个版本！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            isDownloading = true;
            var downloadBtn = sender as Button;
            if (downloadBtn != null) downloadBtn.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            
            UpdateStatus("准备下载...", "#A78BFA");
            ProgressBar.Visibility = Visibility.Visible;
            ProgressText.Text = "0%";

            try
            {
                // 确保目录存在
                if (!Directory.Exists(minecraftDir))
                {
                    UpdateStatus("创建 Minecraft 目录...", "#A78BFA");
                    Directory.CreateDirectory(minecraftDir);
                }

                // 创建必要的子目录
                string[] subDirs = { "versions", "libraries", "assets", "resourcepacks", "saves", "mods", "config" };
                foreach (var subDir in subDirs)
                {
                    Directory.CreateDirectory(Path.Combine(minecraftDir, subDir));
                }

                if (isFirstDownload)
                {
                    SaveDefaultPath(minecraftDir);
                    isFirstDownload = false;
                }

                // 保存游戏目录到配置
                GameDownloadService.SetMinecraftDirectory(minecraftDir);

                // 使用新的下载服务（不再需要传递目录参数）
                _currentCts = new CancellationTokenSource();
                var success = await _downloadService!.StartAsync(selectedVersion, _currentCts.Token);

                if (success)
                {
                    downloadHistory.Insert(0, new DownloadRecord 
                    { 
                        Version = selectedVersion.Id, 
                        Path = minecraftDir, 
                        Time = DateTime.Now, 
                        Success = true 
                    });
                    UpdateDownloadHistory();
                    UpdateStatus("✓ 下载完成!", "#34D399");
                    VersionStatus.Text = "✓";
                    VersionStatus.Foreground = (Brush)FindResource("SuccessBrush");

                    MessageBox.Show($"Minecraft {selectedVersion.Id} 下载完成!\n\n位置: {minecraftDir}",
                        "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("✗ 下载失败: " + ex.Message, "#F87171");
                VersionStatus.Text = "✗";
                VersionStatus.Foreground = (Brush)FindResource("ErrorBrush");
                downloadHistory.Insert(0, new DownloadRecord
                {
                    Version = selectedVersion?.Id ?? "unknown",
                    Path = minecraftDir,
                    Time = DateTime.Now,
                    Success = false,
                    Error = ex.Message
                });
                UpdateDownloadHistory();
                MessageBox.Show("下载失败: " + ex.Message + "\n\n请检查网络连接。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isDownloading = false;
                if (downloadBtn != null) downloadBtn.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
                _currentCts?.Dispose();
                _currentCts = null;
            }
        }

        private void OnDownloadStateChanged(GameDownloadSnapshot snapshot)
        {
            Dispatcher.Invoke(() =>
            {
                if (snapshot.HasTask)
                {
                    ProgressBar.Value = snapshot.Percentage;
                    ProgressText.Text = snapshot.Percentage.ToString("F0") + "%";
                    
                    if (!string.IsNullOrEmpty(snapshot.StageName))
                    {
                        UpdateStatus($"{snapshot.StageName}: {snapshot.Detail}", "#A78BFA");
                    }
                }
                else if (snapshot.IsTerminal)
                {
                    var color = snapshot.Phase == GameDownloadPhase.Completed ? "#34D399" : "#F87171";
                    UpdateStatus(snapshot.Detail, color);
                }
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _currentCts?.Cancel();
            isDownloading = false;
            var downloadBtn = FindName("DownloadButton") as Button;
            if (downloadBtn != null) downloadBtn.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            UpdateStatus("下载已取消", "#FBBF24");
        }

        private void UpdateStatus(string text, string colorKey)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = text;
                try
                {
                    var brush = (Brush)FindResource(colorKey + "Brush");
                    StatusText.Foreground = brush;
                }
                catch
                {
                    StatusText.Foreground = (Brush)FindResource("AccentBrush");
                }
            });
        }

        private void SaveDefaultPath(string path)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SFTLauncher", "config.json");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                var config = new { defaultPath = path };
                File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(config, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void UpdateDownloadHistory()
        {
            Dispatcher.Invoke(() =>
            {
                DownloadHistoryPanel.Children.Clear();
                if (downloadHistory.Count > 0)
                {
                    foreach (var record in downloadHistory.Take(5))
                    {
                        var border = new Border
                        {
                            Background = (Brush)FindResource("BackgroundSecondaryBrush"),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16),
                            Margin = new Thickness(0, 0, 0, 8)
                        };
                        var grid = new Grid();
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var stackPanel = new StackPanel();
                        var titleBlock = new TextBlock
                        {
                            Text = record.Success ? "✓ Minecraft " + record.Version : "✗ Minecraft " + record.Version,
                            Foreground = record.Success ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("ErrorBrush"),
                            FontSize = 13,
                            FontWeight = FontWeights.Bold
                        };
                        var timeBlock = new TextBlock
                        {
                            Text = "时间: " + record.Time.ToString("yyyy-MM-dd HH:mm"),
                            Foreground = (Brush)FindResource("TextMutedBrush"),
                            FontSize = 11,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        stackPanel.Children.Add(titleBlock);
                        stackPanel.Children.Add(timeBlock);
                        Grid.SetColumn(stackPanel, 0);

                        var pathBlock = new TextBlock
                        {
                            Text = Path.GetFileName(record.Path),
                            Foreground = (Brush)FindResource("TextMutedBrush"),
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(pathBlock, 1);

                        grid.Children.Add(stackPanel);
                        grid.Children.Add(pathBlock);
                        border.Child = grid;
                        DownloadHistoryPanel.Children.Add(border);
                    }
                }
            });
        }

        private class DownloadRecord
        {
            public string Version { get; set; } = "";
            public string Path { get; set; } = "";
            public DateTime Time { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; } = "";
        }
    }
}
