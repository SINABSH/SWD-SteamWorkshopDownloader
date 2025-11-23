using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using HtmlAgilityPack;
using Microsoft.Win32;

namespace WpfSteamDownloader
{
    // This is the ViewModel. It contains all the application's logic and state.
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Private Fields
        private readonly string _steamCmdExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd", "steamcmd.exe");
        private readonly string _steamCmdContentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd", "steamapps", "workshop", "content");
        private static readonly HttpClient _client = new HttpClient();
        private bool _isBusy;
        #endregion

        #region Public Properties for Data Binding
        public ObservableCollection<WorkshopItem> WorkshopItems { get; set; }

        private string _appId;
        public string AppId
        {
            get => _appId;
            set { _appId = value; OnPropertyChanged(); }
        }

        private string _addItemText;
        public string AddItemText
        {
            get => _addItemText;
            set { _addItemText = value; OnPropertyChanged(); }
        }

        private WorkshopItem _selectedWorkshopItem;
        public WorkshopItem SelectedWorkshopItem
        {
            get => _selectedWorkshopItem;
            set { _selectedWorkshopItem = value; OnPropertyChanged(); FetchDetailsForSelectedItem(); }
        }

        private string _logText;
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private int _progressMax;
        public int ProgressMax
        {
            get => _progressMax;
            set { _progressMax = value; OnPropertyChanged(); }
        }
        #endregion

        #region ICommand Properties for Buttons
        public ICommand AddItemCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RetryFailedCommand { get; }
        public ICommand SaveListCommand { get; }
        public ICommand LoadListCommand { get; }
        public ICommand OpenFolderCommand { get; }
        #endregion

        public MainViewModel()
        {
            WorkshopItems = new ObservableCollection<WorkshopItem>();

            // Initialize all the commands that the buttons will bind to.
            AddItemCommand = new RelayCommand(async () => await AddItemToListAsync(), () => !_isBusy && !string.IsNullOrWhiteSpace(AddItemText));
            RemoveSelectedCommand = new RelayCommand(RemoveSelectedItem, () => !_isBusy && SelectedWorkshopItem != null);
            ClearAllCommand = new RelayCommand(() => WorkshopItems.Clear(), () => !_isBusy);
            DownloadCommand = new RelayCommand(async () => await StartDownloadProcess(GetUrlsFromList(WorkshopItems)), () => !_isBusy && WorkshopItems.Any());
            RetryFailedCommand = new RelayCommand(async () => await StartDownloadProcess(GetUrlsFromList(WorkshopItems.Where(i => i.Status == "Failed"))), () => !_isBusy && WorkshopItems.Any(i => i.Status == "Failed"));
            SaveListCommand = new RelayCommand(SaveList, () => !_isBusy && WorkshopItems.Any());
            LoadListCommand = new RelayCommand(LoadList, () => !_isBusy);
            OpenFolderCommand = new RelayCommand(OpenDownloadFolder, () => !_isBusy);
        }

        #region Command Methods
        private async Task AddItemToListAsync(string? url = null)
        {
            var urlToUse = (url ?? AddItemText)?.Trim();
            if (string.IsNullOrWhiteSpace(urlToUse))
                return;

            // If called from the UI (no parameter) clear the textbox
            if (url == null)
            {
                AddItemText = "";
            }
            _isBusy = true;
            Log($"AddItemToListAsync called with URL: {urlToUse}");

            var newItem = new WorkshopItem
            {
                Name = "Checking URL...",
                Status = "Pending",
                Url = urlToUse
            };
            WorkshopItems.Add(newItem);
            Log($"WorkshopItem added: {urlToUse}");

            try
            {
                var html = await _client.GetStringAsync(urlToUse);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var collectionNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='collectionChildren']");
                var itemName = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='workshopItemTitle']")?.InnerText.Trim() ?? "Unknown";

                if (collectionNode != null)
                {
                    newItem.Name = $"{itemName} [Collection]";
                    newItem.Type = "Collection";
                    Log($"Item is a collection: {itemName}");
                }
                else
                {
                    newItem.Name = itemName;
                    newItem.Type = "SingleItem";
                    Log($"Item is a single item: {itemName}");
                }
                Log($"Added '{newItem.Name}' to the list.");
            }
            catch (Exception ex)
            {
                newItem.Name = "Failed to add item";
                newItem.Status = "Error";
                Log($"Failed to process URL {urlToUse}: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
                Log("AddItemToListAsync finished.");
            }
        }

        private void RemoveSelectedItem()
        {
            if (SelectedWorkshopItem != null)
            {
                Log($"Removing item: {SelectedWorkshopItem.Name} ({SelectedWorkshopItem.Url})");
                WorkshopItems.Remove(SelectedWorkshopItem);
            }
        }

        private async Task StartDownloadProcess(IEnumerable<string> itemsToDownload)
        {
            var downloadList = itemsToDownload.ToList();
            if (!downloadList.Any()) {
                Log("No items to download.");
                return;
            }

            _isBusy = true;
            Log($"Starting download process for {downloadList.Count} items.");

            // Auto-detect App ID if needed
            if (string.IsNullOrWhiteSpace(AppId))
            {
                var firstUrl = downloadList.FirstOrDefault(u => u.ToLower().StartsWith("http"));
                if (firstUrl != null)
                {
                    AppId = await FetchAppIdFromUrlAsync(firstUrl);
                    Log($"Auto-detected AppId: {AppId}");
                    if (string.IsNullOrEmpty(AppId))
                    {
                        MessageBox.Show("Could not automatically detect App ID. Please enter it manually.", "Error");
                        Log("Failed to auto-detect AppId.");
                        _isBusy = false;
                        return;
                    }
                }
            }

            var finalDownloadQueue = await BuildFinalDownloadQueueAsync(downloadList);
            Log($"Final download queue built. Count: {finalDownloadQueue.Count}");
            if (!finalDownloadQueue.Any())
            {
                Log("Final download queue is empty.");
                _isBusy = false;
                return;
            }

            bool steamCmdReady = await CheckAndInstallSteamCmdAsync();
            Log($"SteamCMD ready: {steamCmdReady}");
            if (!steamCmdReady)
            {
                MessageBox.Show("Could not prepare SteamCMD.", "Error");
                Log("SteamCMD preparation failed.");
                _isBusy = false;
                return;
            }

            ProgressMax = finalDownloadQueue.Count;
            ProgressValue = 0;
            Log($"ProgressMax set to {ProgressMax}");

            var progress = new Progress<(string itemId, string status)>(update =>
            {
                var itemToUpdate = WorkshopItems.FirstOrDefault(item => ParseWorkshopIds(new[] { item.Url }).FirstOrDefault() == update.itemId);

                if (itemToUpdate == null) // Maybe it's an item from an expanded collection
                {
                    var collection = WorkshopItems.FirstOrDefault(i => i.Type == "Collection");
                    if (collection != null)
                    {
                        collection.Status = $"Downloading ({ProgressValue + 1}/{ProgressMax})";
                    }
                }
                else
                {
                    itemToUpdate.Status = update.status;
                    Log($"Download status for {itemToUpdate.Name}: {update.status}");
                }

                if (update.status == "Success" || update.status == "Failed")
                {
                    ProgressValue++;
                    Log($"ProgressValue incremented to {ProgressValue}");
                }
            });

            await Task.Run(() => RunSteamCmd(AppId, finalDownloadQueue, progress));
            Log("Download process finished.");
            _isBusy = false;
        }

        private void SaveList()
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = "MyModList.txt"
            };
            if (sfd.ShowDialog() == true)
            {
                var lines = new List<string> { AppId };
                lines.AddRange(GetUrlsFromList(WorkshopItems));
                File.WriteAllLines(sfd.FileName, lines);
                Log($"Saved list to {sfd.FileName}");
            }
        }

        private async void LoadList()
        {
            var ofd = new OpenFileDialog { Filter = "Text Files (*.txt)|*.txt" };
            if (ofd.ShowDialog() == true)
            {
                WorkshopItems.Clear();
                Log($"Loading list from {ofd.FileName}");
                var lines = File.ReadAllLines(ofd.FileName);
                if (lines.Length >0)
                {
                    AppId = lines[0];
                    Log($"Loaded AppId: {AppId}");
                    foreach (var url in lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        Log($"Loading item URL: {url}");
                        await AddItemToListAsync(url);
                    }
                }
            }
        }

        private void OpenDownloadFolder()
        {
            Log($"OpenDownloadFolder called. Path: {_steamCmdContentPath}");
            if (Directory.Exists(_steamCmdContentPath))
            {
                Process.Start("explorer.exe", _steamCmdContentPath);
                Log("Download folder opened in Explorer.");
            }
            else
            {
                MessageBox.Show("Download folder does not exist yet.", "Not Found");
                Log("Download folder does not exist.");
            }
        }

        private async void FetchDetailsForSelectedItem()
        {
            Log("FetchDetailsForSelectedItem called.");
            if (SelectedWorkshopItem == null)
                return;

            var url = SelectedWorkshopItem.Url;
            Log($"Fetching details for item: {url}");
            try
            {
                var html = await _client.GetStringAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                if (SelectedWorkshopItem.Type == "Collection")
                {
                    SelectedWorkshopItem.Name = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='workshopItemTitle']")?.InnerText.Trim();
                    SelectedWorkshopItem.ImageUrl = htmlDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
                    SelectedWorkshopItem.Author = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='friendBlockContent']")?.InnerText.Trim().Split('\n')[0].Trim();
                    SelectedWorkshopItem.FileSize = htmlDoc.DocumentNode.SelectSingleNode("//span[@class='childCount']")?.InnerText.Trim(); // Item count
                    SelectedWorkshopItem.Visitors = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='detailsStatsContainerLeft']/div[1]")?.InnerText.Trim();
                    SelectedWorkshopItem.DatePosted = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='detailsStatsContainerRight']/div[contains(text(),'@')]")?.InnerText.Trim();
                    Log($"Details fetched for collection: {SelectedWorkshopItem.Name}");
                }
                else // Single item
                {
                    SelectedWorkshopItem.Name = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='workshopItemTitle']")?.InnerText.Trim();
                    SelectedWorkshopItem.ImageUrl = htmlDoc.DocumentNode.SelectSingleNode("//img[@id='previewImage']")?.GetAttributeValue("src", "");
                    SelectedWorkshopItem.Author = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='friendBlockContent']")?.InnerText.Trim().Split('\n')[0].Trim();
                    SelectedWorkshopItem.FileSize = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='detailsStatsContainerRight']/div[1]")?.InnerText.Trim();
                    SelectedWorkshopItem.DatePosted = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='detailsStatsContainerRight']/div[2]")?.InnerText.Trim();
                    SelectedWorkshopItem.Visitors = htmlDoc.DocumentNode.SelectSingleNode("//table[@class='stats_table']/tr[1]/td[1]")?.InnerText.Trim();
                    Log($"Details fetched for item: {SelectedWorkshopItem.Name}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch details for item {url}: {ex.Message}");
            }
        }

        private async Task<List<string>> BuildFinalDownloadQueueAsync(List<string> initialItems)
        {
            Log("BuildFinalDownloadQueueAsync called.");
            var finalQueue = new List<string>();
            foreach (var url in initialItems)
            {
                var item = WorkshopItems.FirstOrDefault(i => i.Url == url);
                if (item?.Type == "Collection")
                {
                    var collectionItems = await GetItemUrlsFromCollectionAsync(url);
                    if (collectionItems != null)
                    {
                        finalQueue.AddRange(collectionItems);
                        Log($"Collection expanded: {url}, items added: {collectionItems.Count}");
                    }
                }
                else
                {
                    finalQueue.Add(url);
                    Log($"Single item added to queue: {url}");
                }
            }
            Log($"Final queue count: {finalQueue.Count}");
            return finalQueue;
        }

        private void RunSteamCmd(string appId, List<string> workshopUrls, IProgress<(string itemId, string status)> progress)
        {
            Log($"RunSteamCmd called. AppId: {appId}, Items: {workshopUrls.Count}");
            string installDir = Path.GetDirectoryName(_steamCmdExePath);
            var parsedIds = ParseWorkshopIds(workshopUrls.ToArray());
            foreach (var itemId in parsedIds)
            {
                progress.Report((itemId, "Downloading..."));
                bool success = false;
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _steamCmdExePath,
                        Arguments = $"+force_install_dir \"{installDir}\" +login anonymous +workshop_download_item {appId} {itemId} +quit",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.OutputDataReceived += (s, e) => { if (e.Data != null && e.Data.Contains("Success. Downloaded item")) success = true; };
                process.ErrorDataReceived += (s, e) => { Log($"[SteamCMD ERR]: {e.Data}"); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Log($"SteamCMD started for item: {itemId}");

                if (process.WaitForExit(600000) && success)
                {
                    progress.Report((itemId, "Success"));
                    Log($"Download succeeded for item: {itemId}");
                }
                else
                {
                    progress.Report((itemId, "Failed"));
                    Log($"Download failed for item: {itemId}");
                    if (!process.HasExited) process.Kill();
                }
            }
            Log("RunSteamCmd finished.");
        }

        private async Task<string> FetchAppIdFromUrlAsync(string url)
        {
            Log($"FetchAppIdFromUrlAsync called for URL: {url}");
            try
            {
                var html = await _client.GetStringAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var node = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='breadcrumbs']//a[contains(@href, '/app/')]");
                if (node != null)
                {
                    var match = Regex.Match(node.GetAttributeValue("href", ""), @"\d+");
                    if (match.Success) {
                        Log($"AppId found: {match.Value}");
                        return match.Value;
                    }
                }
            }
            catch (Exception ex) {
                Log($"Failed to fetch AppId: {ex.Message}");
            }
            return null;
        }

        private async Task<List<string>> GetItemUrlsFromCollectionAsync(string url)
        {
            Log($"GetItemUrlsFromCollectionAsync called for URL: {url}");
            var urls = new List<string>();
            try
            {
                var html = await _client.GetStringAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var nodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='collectionItemDetails']/a");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        urls.Add(node.GetAttributeValue("href", ""));
                        Log($"Collection item URL found: {node.GetAttributeValue("href", "")}");
                    }
                }
            }
            catch (Exception ex) {
                Log($"Failed to get collection items: {ex.Message}");
            }
            return urls;
        }
        #endregion

        #region Helper Methods
        private IEnumerable<string> GetUrlsFromList(IEnumerable<WorkshopItem> items)
        {
            return items.Select(i => i.Url);
        }

        private async Task<bool> CheckAndInstallSteamCmdAsync()
        {
            if (File.Exists(_steamCmdExePath)) return true;
            if (MessageBox.Show("SteamCMD is required. Download it now?", "Setup Required", MessageBoxButton.YesNo) == MessageBoxResult.No) return false;
            try
            {
                string steamCmdDir = Path.GetDirectoryName(_steamCmdExePath);
                if (!Directory.Exists(steamCmdDir)) Directory.CreateDirectory(steamCmdDir);
                var zipPath = Path.Combine(Path.GetTempPath(), "steamcmd.zip");
                File.WriteAllBytes(zipPath, await _client.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"));
                ZipFile.ExtractToDirectory(zipPath, steamCmdDir, true);
                File.Delete(zipPath);
                var process = Process.Start(new ProcessStartInfo(_steamCmdExePath, "+quit") { CreateNoWindow = true });
                await process.WaitForExitAsync();
                return true;
            }
            catch { return false; }
        }

        private List<string> ParseWorkshopIds(string[] lines)
        {
            var idList = new List<string>();
            var regex = new Regex(@"\d+");
            foreach (var line in lines)
            {
                var match = regex.Match(line.Trim());
                if (match.Success) idList.Add(match.Value);
            }
            return idList.Distinct().ToList();
        }

        // Place Log method at the top of the class for global access
        private void Log(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogText += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            });
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion
    }

    #region RelayCommand Helper
    // A standard helper class that allows us to use simple methods as ICommands.
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
    }
    #endregion
}
