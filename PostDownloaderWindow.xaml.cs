using CoomerDownloader.Services;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoomerDownloader
{
    public partial class PostDownloaderWindow : Window
    {
        private readonly CoomerApiService _apiService;
        private readonly Creator _creator;
        private string _downloadPath;
        private CancellationTokenSource? _cancellationTokenSource;
        private ManualResetEventSlim? _pauseEvent;

        public PostDownloaderWindow(Creator creator)
        {
            InitializeComponent();
            _apiService = new CoomerApiService();
            _creator = creator;
            CreatorNameText.Text = $"• {creator.name} ({creator.service})";
            
            // Uygulamanın çalıştığı klasörde creators/[creator_name] klasörü oluştur
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _downloadPath = Path.Combine(appDirectory, "Downloaded", creator.name ?? "downloads");
            DownloadPathText.Text = _downloadPath;
            
            LoadPosts();
        }

        private async void LoadPosts()
        {
            if (!string.IsNullOrEmpty(_creator.id) && !string.IsNullOrEmpty(_creator.service))
            {
                DownloadStatusText.Text = "Loading posts... This may take a while for creators with many posts.";
                DownloadProgressBar.Visibility = Visibility.Visible;
                DownloadProgressBar.IsIndeterminate = true;
                DownloadButton.IsEnabled = false;
                SelectAllButton.IsEnabled = false;
                
                var posts = await _apiService.GetCreatorPosts(_creator.service, _creator.id);
                
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                DownloadStatusText.Text = "";
                DownloadButton.IsEnabled = true;
                SelectAllButton.IsEnabled = true;
                
                if (posts != null)
                {
                    PostsListView.ItemsSource = posts;
                    DownloadStatusText.Text = $"Loaded {posts.Count} posts";
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to load posts!");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Creator information is incomplete, motherfucker!");
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            PostsListView.SelectAll();
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Download Folder",
                FileName = "Select Folder",
                Filter = "Folder|*.folder",
                CheckFileExists = false,
                CheckPathExists = true
            };

            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select download folder";
                folderDialog.SelectedPath = _downloadPath;
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _downloadPath = folderDialog.SelectedPath;
                    DownloadPathText.Text = _downloadPath;
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _pauseEvent?.Reset();
            PauseButton.Visibility = Visibility.Collapsed;
            ResumeButton.Visibility = Visibility.Visible;
            DownloadStatusText.Text = "Download paused";
            CurrentFileText.Text = "Click RESUME to continue downloading...";
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            _pauseEvent?.Set();
            ResumeButton.Visibility = Visibility.Collapsed;
            PauseButton.Visibility = Visibility.Visible;
            DownloadStatusText.Text = "Downloading...";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to cancel the download?", 
                "Cancel Download", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _cancellationTokenSource?.Cancel();
                DownloadStatusText.Text = "Download cancelled by user";
                CurrentFileText.Text = "";
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPosts = PostsListView.SelectedItems;
            if (selectedPosts.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one post to download!", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var downloadFolder = _downloadPath;
            Directory.CreateDirectory(downloadFolder);

            _cancellationTokenSource = new CancellationTokenSource();
            _pauseEvent = new ManualResetEventSlim(true);

            DownloadProgressBar.Visibility = Visibility.Visible;
            StatisticsGrid.Visibility = Visibility.Visible;
            DownloadButton.IsEnabled = false;
            SelectAllButton.IsEnabled = false;
            PauseButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;

            int downloadedFiles = 0;
            int activeDownloads = 0;
            long totalBytesDownloaded = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastUpdateTime = DateTime.Now;
            long lastBytesDownloaded = 0;

            var allFilesToDownload = new List<dynamic>();
            foreach (Post post in selectedPosts)
            {
                foreach (var file in post.AllFiles)
                {
                    if (file != null && !string.IsNullOrEmpty(file.name) && !string.IsNullOrEmpty(file.path))
                        allFilesToDownload.Add(new { name = file.name, path = file.path });
                }
                if (post.attachments != null)
                {
                    foreach (var attachment in post.attachments)
                    {
                        if (attachment != null && !string.IsNullOrEmpty(attachment.name) && !string.IsNullOrEmpty(attachment.path))
                            allFilesToDownload.Add(new { name = attachment.name, path = attachment.path });
                    }
                }
            }

            int totalFiles = allFilesToDownload.Count;
            DownloadProgressBar.Maximum = totalFiles;
            DownloadProgressBar.IsIndeterminate = false;
            FilesCountText.Text = $"0 / {totalFiles}";
            DownloadStatusText.Text = "Starting parallel downloads...";
            CurrentFileText.Text = "Preparing 8 parallel connections...";
            UpdateDownloadStatistics(0, totalFiles, 0, TimeSpan.Zero, ref lastUpdateTime, ref lastBytesDownloaded);

            try
            {
                var downloadTasks = new List<Task>();
                var progressLock = new object();
                var semaphore = new SemaphoreSlim(8); // 8 paralel indirme

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36");
                    client.Timeout = TimeSpan.FromMinutes(5); // Timeout süresini artır

                    foreach (var file in allFilesToDownload)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;

                        await semaphore.WaitAsync(_cancellationTokenSource.Token);
                        downloadTasks.Add(Task.Run(async () =>
                        {
                            var currentActive = Interlocked.Increment(ref activeDownloads);
                            try
                            {
                                _pauseEvent.Wait(_cancellationTokenSource.Token);
                                
                                if (_cancellationTokenSource.Token.IsCancellationRequested)
                                    return;

                                var filePath = Path.Combine(downloadFolder, file.name);
                                // Eğer path zaten tam bir URL ise (https:// ile başlıyorsa) direkt kullan
                                var fileUrl = file.path.StartsWith("http://") || file.path.StartsWith("https://") 
                                    ? file.path 
                                    : $"https://coomer.st{file.path}";

                                var fileBytes = await client.GetByteArrayAsync(fileUrl, _cancellationTokenSource.Token);
                                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes, _cancellationTokenSource.Token);

                                var completed = Interlocked.Increment(ref downloadedFiles);
                                long bytesDownloaded;
                                lock (progressLock)
                                {
                                    totalBytesDownloaded += fileBytes.Length;
                                    bytesDownloaded = totalBytesDownloaded;
                                }

                                Dispatcher.Invoke(() =>
                                {
                                    DownloadProgressBar.Value = completed;
                                    DownloadStatusText.Text = $"Downloading...";
                                    CurrentFileText.Text = $"✅ Completed: {Path.GetFileName(file.name)} ({completed}/{totalFiles})";
                                    UpdateDownloadStatistics(completed, totalFiles, bytesDownloaded, stopwatch.Elapsed,
                                        ref lastUpdateTime, ref lastBytesDownloaded);
                                });
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => CurrentFileText.Text = $"❌ Failed: {Path.GetFileName(file.name)} - {ex.Message}");
                            }
                            finally
                            {
                                Interlocked.Decrement(ref activeDownloads);
                                semaphore.Release();
                            }
                        }));
                    }
                    
                    // HttpClient dispose edilmeden önce tüm task'ların tamamlanmasını bekle
                    await Task.WhenAll(downloadTasks);
                }

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    DownloadStatusText.Text = $"Download cancelled! {downloadedFiles} of {totalFiles} files downloaded.";
                    CurrentFileText.Text = "";
                }
                else
                {
                    DownloadStatusText.Text = $"Download complete! {downloadedFiles} files saved to {downloadFolder}";
                    System.Windows.MessageBox.Show($"Download complete!\n\n{downloadedFiles} files downloaded to:\n{downloadFolder}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                DownloadStatusText.Text = $"Download cancelled! {downloadedFiles} of {totalFiles} files downloaded.";
                CurrentFileText.Text = "";
            }
            catch (Exception ex)
            {
                DownloadStatusText.Text = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                DownloadButton.IsEnabled = true;
                SelectAllButton.IsEnabled = true;
                PauseButton.Visibility = Visibility.Collapsed;
                ResumeButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                
                _cancellationTokenSource?.Dispose();
                _pauseEvent?.Dispose();
            }
        }
        
        private void UpdateDownloadStatistics(int downloadedFiles, int totalFiles, long totalBytes, 
            TimeSpan elapsed, ref DateTime lastUpdateTime, ref long lastBytesDownloaded)
        {
            FilesCountText.Text = $"{downloadedFiles} / {totalFiles}";
            
            double percentage = (double)downloadedFiles / totalFiles * 100;
            ProgressPercentageText.Text = $"{percentage:F1}%";
            
            double sizeMB = totalBytes / (1024.0 * 1024.0);
            DataSizeText.Text = $"{sizeMB:F2} MB";
            
            var now = DateTime.Now;
            var timeDiff = (now - lastUpdateTime).TotalSeconds;
            if (timeDiff >= 0.5)
            {
                long bytesDiff = totalBytes - lastBytesDownloaded;
                double speedMBps = (bytesDiff / (1024.0 * 1024.0)) / timeDiff;
                SpeedText.Text = $"{speedMBps:F2} MB/s";
                
                lastUpdateTime = now;
                lastBytesDownloaded = totalBytes;
                
                // Kalan süre tahmini
                if (speedMBps > 0 && downloadedFiles < totalFiles)
                {
                    int remainingFiles = totalFiles - downloadedFiles;
                    double avgTimePerFile = elapsed.TotalSeconds / downloadedFiles;
                    double estimatedSeconds = avgTimePerFile * remainingFiles;
                    
                    if (estimatedSeconds < 60)
                        TimeRemainingText.Text = $"{estimatedSeconds:F0}s";
                    else if (estimatedSeconds < 3600)
                        TimeRemainingText.Text = $"{(estimatedSeconds / 60):F0}m {(estimatedSeconds % 60):F0}s";
                    else
                        TimeRemainingText.Text = $"{(estimatedSeconds / 3600):F0}h {((estimatedSeconds % 3600) / 60):F0}m";
                }
                else
                {
                    TimeRemainingText.Text = "--:--";
                }
            }
        }
    }
}
