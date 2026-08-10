using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BCCInstaller
{
    public partial class MainWindow : Window
    {
        private readonly InstallerConfig _config;
        private readonly UpdateService _updateService;
        private GitHubReleaseInfo _latestRelease;

        public MainWindow()
        {
            InitializeComponent();

            _config = InstallerConfig.Load();
            _updateService = new UpdateService(_config);

            AutoSelectDetectedRevitVersions();
            UpdateLocalStatus();

            Loaded += MainWindow_Loaded;
        }

        private void AutoSelectDetectedRevitVersions()
        {
            List<string> detected = _updateService.DetectInstalledRevitVersions();

            CbRevit2021.IsChecked = detected.Contains("2021");
            CbRevit2022.IsChecked = detected.Contains("2022");
            CbRevit2023.IsChecked = detected.Contains("2023");
            CbRevit2024.IsChecked = detected.Contains("2024");
            CbRevit2025.IsChecked = detected.Contains("2025");
        }

        private List<string> GetSelectedRevitVersions()
        {
            List<string> selected = new List<string>();
            if (CbRevit2021.IsChecked == true) selected.Add("2021");
            if (CbRevit2022.IsChecked == true) selected.Add("2022");
            if (CbRevit2023.IsChecked == true) selected.Add("2023");
            if (CbRevit2024.IsChecked == true) selected.Add("2024");
            if (CbRevit2025.IsChecked == true) selected.Add("2025");
            return selected;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync();
        }

        private void UpdateLocalStatus()
        {
            string installed = _updateService.GetInstalledVersion();
            InstalledVersionTextBlock.Text = installed;

            List<string> activeManifests = _updateService.GetActiveRevitManifests();

            if (installed == "Не установлен" || activeManifests.Count == 0)
            {
                StatusTextBlock.Text = "Плагин не установлен";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                InstallButton.Content = "УСТАНОВИТЬ ПЛАГИН";
                UninstallButton.IsEnabled = false;
            }
            else
            {
                StatusTextBlock.Text = $"Зарегистрирован для Revit ({string.Join(", ", activeManifests)})";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                InstallButton.Content = "ОБНОВИТЬ ПЛАГИН";
                UninstallButton.IsEnabled = true;
            }
        }

        private async Task CheckUpdatesAsync()
        {
            try
            {
                ProgressStatusTextBlock.Text = "Запрос информации о релизах с GitHub...";
                InstallProgressBar.IsIndeterminate = true;
                CheckUpdatesButton.IsEnabled = false;

                _latestRelease = await _updateService.FetchLatestReleaseAsync();

                OnlineVersionTextBlock.Text = _latestRelease.TagName;
                ReleaseNotesTextBox.Text = $"Релиз: {_latestRelease.Name}\nДата: {_latestRelease.PublishedAt:dd.MM.yyyy HH:mm}\n\n{_latestRelease.Body}";

                string installed = _updateService.GetInstalledVersion();
                if (installed != "Не установлен" && installed.TrimStart('v') == _latestRelease.TagName.TrimStart('v'))
                {
                    StatusTextBlock.Text = "У вас установлена актуальная версия!";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (installed != "Не установлен")
                {
                    StatusTextBlock.Text = "Доступна новая версия на GitHub!";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                }

                ProgressStatusTextBlock.Text = "Проверка завершена.";
            }
            catch (Exception ex)
            {
                OnlineVersionTextBlock.Text = "Ошибка";
                ReleaseNotesTextBox.Text = $"Не удалось проверить обновления на GitHub:\n\n{ex.Message}\n\nВы можете настроить имя пользователя и репозиторий в кнопке 'Настройки GitHub'.";
                ProgressStatusTextBlock.Text = "Ошибка подключения к GitHub.";
            }
            finally
            {
                InstallProgressBar.IsIndeterminate = false;
                InstallProgressBar.Value = 0;
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            List<string> targetVersions = GetSelectedRevitVersions();
            if (targetVersions.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы одну версию Revit для установки (например, Revit 2023 и Revit 2024).", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_latestRelease == null)
            {
                await CheckUpdatesAsync();
                if (_latestRelease == null) return;
            }

            try
            {
                InstallButton.IsEnabled = false;
                CheckUpdatesButton.IsEnabled = false;
                UninstallButton.IsEnabled = false;

                await _updateService.InstallOrUpdateAsync(_latestRelease, targetVersions, (pct, status) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        InstallProgressBar.Value = pct;
                        ProgressStatusTextBlock.Text = status;
                    });
                });

                UpdateLocalStatus();
                MessageBox.Show(this, $"Плагин BIMBCC успешно установлен/обновлен до версии {_latestRelease.TagName}!\n\nЗарегистрирован в %APPDATA% для версий Revit: {string.Join(", ", targetVersions)}.", "BIMBCC Установка", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка установки:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressStatusTextBlock.Text = "Ошибка установки.";
            }
            finally
            {
                InstallButton.IsEnabled = true;
                CheckUpdatesButton.IsEnabled = true;
                UninstallButton.IsEnabled = true;
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            List<string> targetVersions = GetSelectedRevitVersions();
            if (targetVersions.Count == 0) targetVersions = UpdateService.SupportedRevitVersions.ToList();

            if (MessageBox.Show(this, $"Вы действительно хотите удалить плагин BIMBCC из выбранных версий Revit ({string.Join(", ", targetVersions)})?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    _updateService.UninstallPlugin(targetVersions);
                    UpdateLocalStatus();
                    ProgressStatusTextBlock.Text = "Плагин успешно удален.";
                    MessageBox.Show(this, "Плагин BIMBCC успешно удален из выбранных версий Revit.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка удаления:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            string currentRepo = $"{_config.GitHubOwner}/{_config.GitHubRepo}";
            string input = Microsoft.VisualBasic.Interaction.InputBox("Укажите репозиторий GitHub в формате 'Пользователь/Репозиторий':", "Настройки GitHub", currentRepo);

            if (!string.IsNullOrEmpty(input) && input.Contains("/"))
            {
                string[] parts = input.Split('/');
                _config.GitHubOwner = parts[0].Trim();
                _config.GitHubRepo = parts[1].Trim();
                InstallerConfig.Save(_config);

                _ = CheckUpdatesAsync();
            }
        }
    }
}
