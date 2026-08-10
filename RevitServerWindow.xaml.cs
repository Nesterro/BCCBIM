using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class RevitServerWindow : Window
    {
        private readonly Autodesk.Revit.ApplicationServices.Application _revitApp;
        private string _serverAddress;
        private string _serverVersion;
        private RevitServerClient _client;

        // Folders bound to TreeView
        public ObservableCollection<FolderViewModel> RootFolders { get; }

        // Models in the currently selected folder
        public ObservableCollection<ModelFileViewModel> CurrentFolderModels { get; }

        // Global storage of selected models across all folders (Key: full RSN path, Value: Model view model)
        private readonly Dictionary<string, ModelFileViewModel> _selectedModels = new Dictionary<string, ModelFileViewModel>();

        public RevitServerWindow(Autodesk.Revit.ApplicationServices.Application revitApp)
        {
            InitializeComponent();
            SetWindowIcon();

            _revitApp = revitApp;
            
            RootFolders = new ObservableCollection<FolderViewModel>();
            FoldersTreeView.ItemsSource = RootFolders;

            CurrentFolderModels = new ObservableCollection<ModelFileViewModel>();
            ModelsListBox.ItemsSource = CurrentFolderModels;

            // Load saved destination path from temporary user settings if any, or default to Documents
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BIMBCC_Downloads");
            DestinationFolderTextBox.Text = defaultPath;
        }

        private void SetWindowIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("BCCPlugIn.Resources.pdf_export_icon.png"))
                {
                    if (stream != null)
                    {
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch
            {
                // Fallback ignored
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
                // Initialize client
                _client?.Dispose();
                _client = new RevitServerClient(_serverAddress, _serverVersion);

                // Verify connection
                StatusTextBlock.Text = "Проверка соединения...";
                var props = await Task.Run(() => _client.CheckConnectionAsync());
                
                StatusTextBlock.Text = "Соединение установлено. Загрузка корневого каталога...";

                // Get root contents
                var rootContents = await Task.Run(() => _client.GetContentsAsync("|"));

                // Add root folders to the tree view
                if (rootContents?.Folders != null)
                {
                    foreach (var folder in rootContents.Folders)
                    {
                        bool hasSubfolders = folder.FolderCount > 0;
                        RootFolders.Add(new FolderViewModel(folder.Name, folder.Name, hasSubfolders));
                    }
                }

                StatusTextBlock.Text = $"Подключено к {props.ServerName} (Версия {props.ServerVersion})";
            }
            catch (Exception ex)
            {
                _client?.Dispose();
                _client = null;
                StatusTextBlock.Text = "Ошибка подключения";
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

                // Get contents of this folder
                var contents = await Task.Run(() => _client.GetContentsAsync(folder.ServerRelativePath));

                // Clear dummy item
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
                // Re-add dummy if it failed so expansion can be tried again
                if (folder.SubFolders.Count == 0)
                {
                    folder.SubFolders.Add(null);
                }
                MessageBox.Show(this, $"Не удалось загрузить подпапки:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                
                // Get contents of this folder
                var contents = await Task.Run(() => _client.GetContentsAsync(selectedFolder.ServerRelativePath));

                CurrentFolderModels.Clear();

                if (contents?.Models != null)
                {
                    foreach (var model in contents.Models)
                    {
                        var modelVm = new ModelFileViewModel(model.Name, selectedFolder.ServerRelativePath, model.Size);
                        
                        // Check if it was already selected previously in this session
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
                MessageBox.Show(this, $"Не удалось загрузить модели:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string localFolder = DestinationFolderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(localFolder))
            {
                MessageBox.Show(this, "Пожалуйста, выберите локальную папку для сохранения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedModels.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы одну модель для скачивания.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create target folder if it doesn't exist
            try
            {
                if (!Directory.Exists(localFolder))
                {
                    Directory.CreateDirectory(localFolder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось создать директорию:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Lock UI elements during download
            SetUiEnabled(false);

            int total = _selectedModels.Count;
            DownloadProgressBar.Maximum = total;
            DownloadProgressBar.Value = 0;

            int successCount = 0;
            int failCount = 0;
            List<string> errors = new List<string>();

            int currentIdx = 0;
            foreach (var entry in _selectedModels)
            {
                string rsnPath = entry.Key;
                ModelFileViewModel model = entry.Value;
                currentIdx++;

                StatusTextBlock.Text = $"Скачивание {currentIdx} из {total}: {model.Name}...";
                DoEvents();

                try
                {
                    // Convert user visible path to ModelPath for CopyModel
                    ModelPath sourceModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(rsnPath);
                    string destFilePath = Path.Combine(localFolder, model.Name);

                    // Copy model via Revit API (Synchronous on the main thread)
                    _revitApp.CopyModel(sourceModelPath, destFilePath, true);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    errors.Add($"{model.Name}: {ex.Message}");
                }

                DownloadProgressBar.Value = currentIdx;
                DoEvents();
            }

            // Finished
            StatusTextBlock.Text = "Скачивание завершено";
            SetUiEnabled(true);

            string msg = $"Скачивание успешно завершено!\n\nУспешно скачано: {successCount}";
            if (failCount > 0)
            {
                msg += $"\nОшибок при скачивании: {failCount}\n\nПодробности ошибок:\n" + string.Join("\n", errors);
                MessageBox.Show(this, msg, "Результаты скачивания", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, msg, "Результаты скачивания", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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

        // Helper: Generates user visible path, e.g. RSN://127.0.0.1/FolderA/Model.rvt
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

            // Enable download only if we have selected models and connected server
            DownloadButton.IsEnabled = count > 0 && !string.IsNullOrWhiteSpace(DestinationFolderTextBox.Text);
        }

        private void SetUiEnabled(bool enabled)
        {
            FoldersTreeView.IsEnabled = enabled;
            ModelsListBox.IsEnabled = enabled;
            ConnectButton.IsEnabled = enabled;
            ServerAddressTextBox.IsEnabled = enabled;
            ServerVersionComboBox.IsEnabled = enabled;
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

        // Helper to push frame and refresh UI during synchronous main thread execution
        public static void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object f)
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }
    }
}
