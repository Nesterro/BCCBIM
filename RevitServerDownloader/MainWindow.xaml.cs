using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RevitServerDownloader
{
    public partial class MainWindow : Window
    {
        private string _serverAddress;
        private string _serverVersion;
        private RevitServerClient _client;

        // Folders bound to TreeView
        public ObservableCollection<FolderViewModel> RootFolders { get; }

        // Models in the currently selected folder
        public ObservableCollection<ModelFileViewModel> CurrentFolderModels { get; }

        // Global storage of selected models across all folders (Key: RSN path, Value: Model view model)
        private readonly Dictionary<string, ModelFileViewModel> _selectedModels = new Dictionary<string, ModelFileViewModel>();

        public MainWindow()
        {
            InitializeComponent();
            
            RootFolders = new ObservableCollection<FolderViewModel>();
            FoldersTreeView.ItemsSource = RootFolders;

            CurrentFolderModels = new ObservableCollection<ModelFileViewModel>();
            ModelsListBox.ItemsSource = CurrentFolderModels;

            // Set default destination path
            string defaultDest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitServer_Downloads");
            DestinationFolderTextBox.Text = defaultDest;

            // Run auto-detect for RevitServerTool.exe
            AutoDetectToolPath();
        }

        private void LogLine(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBox.ScrollToEnd();
            }));
        }

        private void AutoDetectToolPath()
        {
            string[] commonVersions = { "2026", "2025", "2024", "2023", "2022", "2021", "2020" };
            string baseDir = @"C:\Program Files\Autodesk";

            foreach (var version in commonVersions)
            {
                // 1. Check user machine path (Revit Client installation)
                string path1 = Path.Combine(baseDir, $"Revit {version}", "RevitServerToolCommand", "RevitServerTool.exe");
                if (File.Exists(path1))
                {
                    ToolPathTextBox.Text = path1;
                    SetSelectedVersion(version);
                    LogLine($"Автоопределение: Найден RevitServerTool {version} по пути {path1}");
                    return;
                }

                // 2. Check Revit Server tools installation path
                string path2 = Path.Combine(baseDir, $"Revit Server {version}", "tools", "RevitServerToolCommand", "RevitServerTool.exe");
                if (File.Exists(path2))
                {
                    ToolPathTextBox.Text = path2;
                    SetSelectedVersion(version);
                    LogLine($"Автоопределение: Найден RevitServerTool (Сервер) {version} по пути {path2}");
                    return;
                }
            }

            LogLine("Не удалось автоматически найти RevitServerTool.exe. Пожалуйста, укажите путь вручную.");
        }

        private void SetSelectedVersion(string version)
        {
            foreach (ComboBoxItem item in ServerVersionComboBox.Items)
            {
                if (item.Content.ToString() == version)
                {
                    ServerVersionComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void AutoDetectTool_Click(object sender, RoutedEventArgs e)
        {
            AutoDetectToolPath();
        }

        private void BrowseTool_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "RevitServerTool.exe|RevitServerTool.exe|Все исполняемые файлы (*.exe)|*.exe",
                Title = "Укажите путь к RevitServerTool.exe"
            };

            if (ofd.ShowDialog() == true)
            {
                ToolPathTextBox.Text = ofd.FileName;
                LogLine($"Выбран путь к утилите: {ofd.FileName}");
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            _serverAddress = ServerAddressTextBox.Text.Trim();
            var versionItem = ServerVersionComboBox.SelectedItem as ComboBoxItem;
            _serverVersion = versionItem?.Content?.ToString();

            if (string.IsNullOrWhiteSpace(_serverAddress))
            {
                MessageBox.Show(this, "Пожалуйста, введите адрес Revit Server.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConnectButton.IsEnabled = false;
            StatusTextBlock.Text = "Подключение к серверу...";
            DownloadProgressBar.Value = 0;
            
            RootFolders.Clear();
            CurrentFolderModels.Clear();
            _selectedModels.Clear();
            UpdateSummaryText();

            try
            {
                _client?.Dispose();
                _client = new RevitServerClient(_serverAddress, _serverVersion);

                LogLine($"Подключение к http://{_serverAddress} (Версия {_serverVersion})...");
                
                var props = await Task.Run(() => _client.CheckConnectionAsync());
                
                LogLine($"Успешное подключение. Имя сервера: {props.ServerName}, Версия API: {props.ServerVersion}");
                StatusTextBlock.Text = "Загрузка корневого каталога...";

                var rootContents = await Task.Run(() => _client.GetContentsAsync("|"));

                if (rootContents?.Folders != null)
                {
                    foreach (var folder in rootContents.Folders)
                    {
                        bool hasSubfolders = folder.FolderCount > 0;
                        RootFolders.Add(new FolderViewModel(folder.Name, folder.Name, hasSubfolders));
                    }
                }

                StatusTextBlock.Text = $"Подключено к {props.ServerName}";
            }
            catch (Exception ex)
            {
                _client?.Dispose();
                _client = null;
                StatusTextBlock.Text = "Ошибка подключения";
                LogLine($"Ошибка подключения: {ex.Message}");
                MessageBox.Show(this, $"Не удалось подключиться к Revit Server:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            var treeViewItem = e.OriginalSource as TreeViewItem;
            var folder = treeViewItem?.Header as FolderViewModel;

            if (folder == null || folder.IsLoaded || _client == null) return;

            try
            {
                StatusTextBlock.Text = $"Загрузка папки {folder.Name}...";

                var contents = await Task.Run(() => _client.GetContentsAsync(folder.ServerRelativePath));

                folder.SubFolders.Clear();

                if (contents?.Folders != null)
                {
                    foreach (var subFolder in contents.Folders)
                    {
                        bool hasSub = subFolder.FolderCount > 0;
                        string relativePath = $"{folder.ServerRelativePath}|{subFolder.Name}";
                        folder.SubFolders.Add(new FolderViewModel(subFolder.Name, relativePath, hasSub));
                    }
                }

                folder.IsLoaded = true;
                StatusTextBlock.Text = "Каталог обновлен";
            }
            catch (Exception ex)
            {
                if (folder.SubFolders.Count == 0)
                {
                    folder.SubFolders.Add(null); // restore dummy
                }
                LogLine($"Ошибка загрузки подпапок {folder.Name}: {ex.Message}");
            }
        }

        private async void FoldersTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedFolder = FoldersTreeView.SelectedItem as FolderViewModel;
            if (selectedFolder == null || _client == null)
            {
                SelectionHelpersPanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            try
            {
                StatusTextBlock.Text = $"Получение моделей из {selectedFolder.Name}...";
                var contents = await Task.Run(() => _client.GetContentsAsync(selectedFolder.ServerRelativePath));

                CurrentFolderModels.Clear();

                if (contents?.Models != null)
                {
                    foreach (var model in contents.Models)
                    {
                        var modelVm = new ModelFileViewModel(model.Name, selectedFolder.ServerRelativePath, model.Size);
                        
                        string rsnPath = GetRsnPath(modelVm);
                        if (_selectedModels.ContainsKey(rsnPath))
                        {
                            modelVm.IsSelected = true;
                        }

                        CurrentFolderModels.Add(modelVm);
                    }
                }

                SelectionHelpersPanel.Visibility = CurrentFolderModels.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                StatusTextBlock.Text = $"Найдено моделей: {CurrentFolderModels.Count}";
            }
            catch (Exception ex)
            {
                LogLine($"Ошибка загрузки моделей: {ex.Message}");
            }
        }

        private void ModelCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var modelVm = checkBox?.DataContext as ModelFileViewModel;
            if (modelVm == null) return;

            string rsnPath = GetRsnPath(modelVm);

            if (modelVm.IsSelected)
            {
                _selectedModels[rsnPath] = modelVm;
            }
            else
            {
                _selectedModels.Remove(rsnPath);
            }

            UpdateSummaryText();
        }

        private void SelectAllModels_Click(object sender, RoutedEventArgs e)
        {
            foreach (var model in CurrentFolderModels)
            {
                model.IsSelected = true;
                string rsnPath = GetRsnPath(model);
                _selectedModels[rsnPath] = model;
            }
            UpdateSummaryText();
        }

        private void SelectNoneModels_Click(object sender, RoutedEventArgs e)
        {
            foreach (var model in CurrentFolderModels)
            {
                model.IsSelected = false;
                string rsnPath = GetRsnPath(model);
                _selectedModels.Remove(rsnPath);
            }
            UpdateSummaryText();
        }

        private void BrowseDestinationFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для сохранения моделей";
                dialog.ShowNewFolderButton = true;
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    DestinationFolderTextBox.Text = dialog.SelectedPath;
                    UpdateSummaryText();
                }
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string localFolder = DestinationFolderTextBox.Text.Trim();
            string toolPath = ToolPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
            {
                MessageBox.Show(this, "Пожалуйста, укажите корректный путь к RevitServerTool.exe.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(localFolder))
            {
                MessageBox.Show(this, "Пожалуйста, выберите папку для сохранения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedModels.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы одну модель.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetUiEnabled(false);
            LogLine("--- Запуск процесса скачивания моделей ---");

            int total = _selectedModels.Count;
            DownloadProgressBar.Maximum = total;
            DownloadProgressBar.Value = 0;

            var modelsToDownload = _selectedModels.Values.ToList();

            // Run download loop in a background thread to prevent UI freezing
            await Task.Run(() =>
            {
                int successCount = 0;
                int failCount = 0;
                
                for (int i = 0; i < modelsToDownload.Count; i++)
                {
                    var model = modelsToDownload[i];
                    int index = i + 1;

                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = $"Скачивание {index} из {total}: {model.Name}...";
                    });

                    LogLine($"[{index}/{total}] Запуск загрузки {model.Name}...");

                    // In RevitServerTool, the path should use backslashes and exclude the server prefix
                    string relativeModelPath = string.IsNullOrWhiteSpace(model.FolderPath)
                        ? model.Name
                        : Path.Combine(model.FolderPath.Replace('|', '\\'), model.Name);

                    string destFilePath = Path.Combine(localFolder, model.Name);

                    try
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = toolPath,
                            // Arguments format: createLocalRVT <modelPath> -s <serverName> -d <destination> -o
                            Arguments = $"createLocalRVT \"{relativeModelPath}\" -s \"{_serverAddress}\" -d \"{destFilePath}\" -o",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = new Process { StartInfo = startInfo })
                        {
                            process.OutputDataReceived += (s, args) =>
                            {
                                if (!string.IsNullOrEmpty(args.Data)) LogLine($"[Console] {args.Data}");
                            };
                            process.ErrorDataReceived += (s, args) =>
                            {
                                if (!string.IsNullOrEmpty(args.Data)) LogLine($"[Console Error] {args.Data}");
                            };

                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            process.WaitForExit();

                            if (process.ExitCode == 0)
                            {
                                LogLine($"[{index}/{total}] Успешно скачано: {model.Name}");
                                successCount++;
                            }
                            else
                            {
                                LogLine($"[{index}/{total}] Ошибка при скачивании {model.Name}. Код выхода: {process.ExitCode}");
                                failCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogLine($"[{index}/{total}] Исключение при скачивании {model.Name}: {ex.Message}");
                        failCount++;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressBar.Value = index;
                    });
                }

                LogLine($"--- Процесс скачивания завершен. Успешно: {successCount}, Ошибок: {failCount} ---");
                
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = "Скачивание завершено";
                    SetUiEnabled(true);
                    
                    string finishMsg = $"Скачивание завершено!\n\nУспешно скачано: {successCount}";
                    if (failCount > 0)
                    {
                        finishMsg += $"\nОшибок: {failCount}\n\nСмотрите подробности в логе процесса.";
                        MessageBox.Show(this, finishMsg, "Результаты скачивания", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(this, finishMsg, "Результаты скачивания", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _client?.Dispose();
            base.OnClosed(e);
        }

        private string GetRsnPath(ModelFileViewModel model)
        {
            string pathPart = string.IsNullOrWhiteSpace(model.FolderPath) 
                ? "" 
                : model.FolderPath.Replace('|', '/') + "/";
            return $"RSN://{_serverAddress}/{pathPart}{model.Name}";
        }

        private void UpdateSummaryText()
        {
            int count = _selectedModels.Count;
            long totalBytes = _selectedModels.Values.Sum(m => m.SizeBytes);
            
            string sizeFormatted = FormatSize(totalBytes);
            SelectedSummaryTextBlock.Text = $"Выбрано моделей: {count} ({sizeFormatted})";

            DownloadButton.IsEnabled = count > 0 && !string.IsNullOrWhiteSpace(DestinationFolderTextBox.Text);
        }

        private void SetUiEnabled(bool enabled)
        {
            FoldersTreeView.IsEnabled = enabled;
            ModelsListBox.IsEnabled = enabled;
            ConnectButton.IsEnabled = enabled;
            ServerAddressTextBox.IsEnabled = enabled;
            ServerVersionComboBox.IsEnabled = enabled;
            ToolPathTextBox.IsEnabled = enabled;
            DestinationFolderTextBox.IsEnabled = enabled;
            DownloadButton.IsEnabled = enabled;
            CancelButton.IsEnabled = enabled;
        }

        private string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 Б";
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            double size = bytes;
            int unitIdx = 0;
            while (size >= 1024 && unitIdx < units.Length - 1)
            {
                size /= 1024;
                unitIdx++;
            }
            return $"{size:F1} {units[unitIdx]}";
        }
    }
}
