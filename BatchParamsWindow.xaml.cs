using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class BatchParamsWindow : Window
    {
        private readonly Document _doc;

        // STEP 1: Source FOP parameters
        private List<SharedParamItem> _allSourceFopParams = new List<SharedParamItem>();
        private ObservableCollection<SharedParamItem> _filteredSourceFopParams = new ObservableCollection<SharedParamItem>();

        // STEP 2: Prepared staged parameters for batch write
        private ObservableCollection<SharedParamItem> _stagedParams = new ObservableCollection<SharedParamItem>();

        // Categories
        private List<CategoryItem> _allCategories = new List<CategoryItem>();
        private ObservableCollection<CategoryItem> _filteredCategories = new ObservableCollection<CategoryItem>();

        public BatchParamsWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            SetupDocumentMode();
            PopulateBindingGroups();
            LoadCategories();
            AutoDetectActiveSharedParamFile();

            StagedParamsDataGrid.ItemsSource = _stagedParams;
        }

        private void SetupDocumentMode()
        {
            if (_doc != null && _doc.IsFamilyDocument)
            {
                ModeBadgeTextBlock.Text = "РЕЖИМ: СЕМЕЙСТВО (.rfa)";
                ModeBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
                ModeBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 125, 50));
                ModeBadgeTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 125, 50));

                CategoriesBorder.Visibility = System.Windows.Visibility.Collapsed;
                FamilyInfoBorder.Visibility = System.Windows.Visibility.Visible;

                string catName = _doc.OwnerFamily != null && _doc.OwnerFamily.FamilyCategory != null
                    ? _doc.OwnerFamily.FamilyCategory.Name
                    : "Общие модели";

                FamilyCategoryTextBlock.Text = $"Категория семейства: {catName}";
                ExecuteButton.Content = "ЗАПИСАТЬ ПОДГОТОВЛЕННЫЕ ПАРАМЕТРЫ В СЕМЕЙСТВО";
            }
            else
            {
                ModeBadgeTextBlock.Text = "РЕЖИМ: ПРОЕКТ (.rvt)";
                CategoriesBorder.Visibility = System.Windows.Visibility.Visible;
                FamilyInfoBorder.Visibility = System.Windows.Visibility.Collapsed;
                ExecuteButton.Content = "ЗАПИСАТЬ ПОДГОТОВЛЕННЫЕ ПАРАМЕТРЫ В ПРОЕКТ";
            }
        }

        private void PopulateBindingGroups()
        {
            var groups = BatchParamsEngine.GetBindingGroups();
            BatchGroupComboBox.ItemsSource = groups;
            BatchGroupComboBox.DisplayMemberPath = "Name";
            BatchGroupComboBox.SelectedValuePath = "BuiltInGroup";
            BatchGroupComboBox.SelectedIndex = 0;
        }

        private void LoadCategories()
        {
            if (_doc == null || _doc.IsFamilyDocument) return;

            _allCategories = BatchParamsEngine.GetDocumentCategories(_doc);
            foreach (var cat in _allCategories)
            {
                cat.PropertyChanged += (s, e) => UpdateSelectedCategoriesCount();
            }
            FilterCategories();
        }

        private void AutoDetectActiveSharedParamFile()
        {
            try
            {
                string fopPath = _doc.Application.SharedParametersFilename;
                if (!string.IsNullOrEmpty(fopPath) && File.Exists(fopPath))
                {
                    FopFilePathTextBox.Text = fopPath;
                    LoadSharedParameterFile(fopPath);
                }
                else
                {
                    FopFilePathTextBox.Text = "ФОП не задан в Revit. Нажмите 'Обзор ФОП (.txt)'...";
                }
            }
            catch
            {
                FopFilePathTextBox.Text = "Нажмите 'Обзор ФОП (.txt)' для выбора файла...";
            }
        }

        private void LoadSharedParameterFile(string filePath)
        {
            _allSourceFopParams = SharedParamParser.ParseSharedParameterFile(_doc, filePath);

            List<string> groupNames = new List<string> { "Все группы" };
            groupNames.AddRange(_allSourceFopParams.Select(p => p.GroupName).Distinct().OrderBy(g => g));

            FopGroupsListBox.ItemsSource = groupNames;
            FopGroupsListBox.SelectedIndex = 0;

            FilterSourceFopParams();
        }

        private void BrowseFop_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Файл общих параметров (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Выберите файл общих параметров (ФОП)"
            };

            if (ofd.ShowDialog() == true)
            {
                FopFilePathTextBox.Text = ofd.FileName;
                LoadSharedParameterFile(ofd.FileName);
            }
        }

        private void FilterSourceFopParams()
        {
            string selectedGroup = FopGroupsListBox.SelectedItem as string;
            string search = ParamSearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";

            var query = _allSourceFopParams.AsEnumerable();

            if (!string.IsNullOrEmpty(selectedGroup) && selectedGroup != "Все группы")
            {
                query = query.Where(p => p.GroupName == selectedGroup);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.ToLowerInvariant().Contains(search) || p.Guid.ToString().ToLowerInvariant().Contains(search));
            }

            _filteredSourceFopParams = new ObservableCollection<SharedParamItem>(query);
            SourceFopDataGrid.ItemsSource = _filteredSourceFopParams;
        }

        private void FopGroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterSourceFopParams();
        }

        private void ParamSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterSourceFopParams();
        }

        private void SelectAllSourceFop_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _filteredSourceFopParams) p.IsSelected = true;
        }

        private void DeselectAllSourceFop_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _filteredSourceFopParams) p.IsSelected = false;
        }

        private void SourceFopCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            if (cb == null) return;
            SharedParamItem currentItem = cb.DataContext as SharedParamItem;
            if (currentItem == null) return;

            bool newState = cb.IsChecked ?? false;
            var selectedRows = SourceFopDataGrid.SelectedItems.Cast<SharedParamItem>().ToList();
            if (selectedRows.Contains(currentItem) && selectedRows.Count > 1)
            {
                foreach (var item in selectedRows)
                {
                    item.IsSelected = newState;
                }
            }
        }

        private void StagedParamCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            if (cb == null) return;
            SharedParamItem currentItem = cb.DataContext as SharedParamItem;
            if (currentItem == null) return;

            bool newState = cb.IsChecked ?? false;
            var selectedRows = StagedParamsDataGrid.SelectedItems.Cast<SharedParamItem>().ToList();
            if (selectedRows.Contains(currentItem) && selectedRows.Count > 1)
            {
                foreach (var item in selectedRows)
                {
                    item.IsSelected = newState;
                }
            }
            UpdateStagedParamsCount();
        }

        // STEP 1 -> STEP 2: STAGING PARAMETERS
        private void StageSelectedFopParams_Click(object sender, RoutedEventArgs e)
        {
            // Prioritize parameters checked with [✓] checkbox over blue row selection
            var selectedInFop = _allSourceFopParams.Where(p => p.IsSelected).ToList();
            if (selectedInFop.Count == 0)
            {
                selectedInFop = SourceFopDataGrid.SelectedItems.Cast<SharedParamItem>().ToList();
            }

            if (selectedInFop.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы один параметр из ФОП (отметьте галочкой [✓] или выделите строки).", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int addedCount = 0;
            foreach (var source in selectedInFop)
            {
                if (!_stagedParams.Any(p => p.Guid == source.Guid || string.Equals(p.Name, source.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    SharedParamItem stagedItem = new SharedParamItem
                    {
                        IsSelected = true,
                        Name = source.Name,
                        Guid = source.Guid,
                        GroupName = source.GroupName,
                        DataType = source.DataType,
                        IsInstance = true,
                        BindingGroup = BuiltInParameterGroup.PG_DATA,
                        DefaultValue = source.DefaultValue,
                        Definition = source.Definition
                    };
                    stagedItem.PropertyChanged += (s, ev) => UpdateStagedParamsCount();
                    _stagedParams.Add(stagedItem);
                    addedCount++;
                }
            }

            UpdateStagedParamsCount();
            ProgressStatusTextBlock.Text = $"Преддобавлено {addedCount} параметров в список к записи. Теперь настройте их типы и значения!";
        }

        // TOOLBAR & STAGED PARAMETERS ACTIONS
        private void AddCustomParam_Click(object sender, RoutedEventArgs e)
        {
            SharedParamItem customParam = new SharedParamItem
            {
                IsSelected = true,
                Name = $"Пользовательский_Параметр_{_stagedParams.Count + 1}",
                GroupName = "Пользовательские",
                DataType = "TEXT",
                IsInstance = true,
                BindingGroup = BuiltInParameterGroup.PG_DATA,
                IsCustomParam = true
            };
            customParam.PropertyChanged += (s, ev) => UpdateStagedParamsCount();

            _stagedParams.Add(customParam);
            UpdateStagedParamsCount();

            StagedParamsDataGrid.SelectedItem = customParam;
            StagedParamsDataGrid.ScrollIntoView(customParam);
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_stagedParams.Count == 0)
            {
                MessageBox.Show(this, "Нет подготовленных параметров для сохранения в пресет.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Пресет параметров (*.json)|*.json",
                Title = "Сохранить пресет параметров ModPlus/BIMBCC",
                FileName = "Пресет_Параметров.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    BatchParamsPreset preset = new BatchParamsPreset
                    {
                        Name = Path.GetFileNameWithoutExtension(sfd.FileName),
                        Items = _stagedParams.ToList()
                    };
                    BatchParamsPreset.SavePreset(sfd.FileName, preset);
                    MessageBox.Show(this, $"Пресет с {_stagedParams.Count} параметрами успешно сохранен!", "BIMBCC Пресет", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка сохранения пресета:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Пресет параметров (*.json)|*.json",
                Title = "Загрузить пресет параметров ModPlus/BIMBCC"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var preset = BatchParamsPreset.LoadPreset(ofd.FileName);
                    if (preset != null && preset.Items.Count > 0)
                    {
                        foreach (var item in preset.Items)
                        {
                            item.IsSelected = true;
                            item.PropertyChanged += (s, ev) => UpdateStagedParamsCount();
                            _stagedParams.Add(item);
                        }
                        UpdateStagedParamsCount();
                        MessageBox.Show(this, $"Загружено {preset.Items.Count} параметров из пресета {preset.Name}!", "BIMBCC Пресет", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка загрузки пресета:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
                Title = "Импортировать список параметров из CSV/Excel"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var imported = BatchParamsPreset.ImportFromCsv(ofd.FileName);
                    if (imported.Count > 0)
                    {
                        foreach (var item in imported)
                        {
                            item.PropertyChanged += (s, ev) => UpdateStagedParamsCount();
                            _stagedParams.Add(item);
                        }
                        UpdateStagedParamsCount();
                        MessageBox.Show(this, $"Импортировано {imported.Count} параметров из CSV!", "Импорт CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка импорта CSV:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_stagedParams.Count == 0) return;

            Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv",
                Title = "Экспортировать список параметров в CSV/Excel",
                FileName = "Экспорт_Параметров.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    BatchParamsPreset.ExportToCsv(sfd.FileName, _stagedParams.ToList());
                    MessageBox.Show(this, "Список параметров успешно экспортирован в CSV!", "Экспорт CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка экспорта CSV:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteStagedParams_Click(object sender, RoutedEventArgs e)
        {
            var selectedRows = StagedParamsDataGrid.SelectedItems.Cast<SharedParamItem>().ToList();
            if (selectedRows.Count == 0) selectedRows = _stagedParams.Where(p => p.IsSelected).ToList();

            foreach (var item in selectedRows)
            {
                _stagedParams.Remove(item);
            }
            UpdateStagedParamsCount();
        }

        private void DeleteSingleStagedParamRow_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            SharedParamItem item = btn?.DataContext as SharedParamItem;
            if (item != null)
            {
                _stagedParams.Remove(item);
                UpdateStagedParamsCount();
            }
        }

        private void ClearStagedParams_Click(object sender, RoutedEventArgs e)
        {
            if (_stagedParams.Count == 0) return;

            if (MessageBox.Show(this, "Очистить список подготовленных параметров?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _stagedParams.Clear();
                UpdateStagedParamsCount();
            }
        }

        private void SelectAllStaged_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _stagedParams) p.IsSelected = true;
            UpdateStagedParamsCount();
        }

        private void DeselectAllStaged_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _stagedParams) p.IsSelected = false;
            UpdateStagedParamsCount();
        }

        private void StagedParamsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStagedParamsCount();
        }

        private List<SharedParamItem> GetTargetStagedParams(SharedParamItem triggerItem = null)
        {
            var blueSelectedItems = StagedParamsDataGrid.SelectedItems.Cast<SharedParamItem>().ToList();

            // Priority 1: If user highlighted rows in DataGrid (blue selection), target those exact rows regardless of checkboxes
            if (blueSelectedItems.Count > 0)
            {
                if (triggerItem != null && !blueSelectedItems.Contains(triggerItem))
                {
                    blueSelectedItems.Add(triggerItem);
                }
                return blueSelectedItems;
            }

            // Priority 2: Single item trigger
            if (triggerItem != null)
            {
                return new List<SharedParamItem> { triggerItem };
            }

            // Priority 3: Checkbox checked items
            var checkedItems = _stagedParams.Where(p => p.IsSelected).ToList();
            return checkedItems.Count > 0 ? checkedItems : _stagedParams.ToList();
        }

        private void StagedRowBindingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cb = sender as ComboBox;
            if (cb == null) return;

            SharedParamItem clickedItem = cb.DataContext as SharedParamItem;
            if (clickedItem == null) return;

            string selectedValue = (cb.SelectedItem as ComboBoxItem)?.Content as string;
            if (!string.IsNullOrEmpty(selectedValue))
            {
                clickedItem.BindingTypeName = selectedValue;

                var targetRows = GetTargetStagedParams(clickedItem);
                foreach (var item in targetRows)
                {
                    item.BindingTypeName = selectedValue;
                }
            }
        }

        private void StagedRowGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cb = sender as ComboBox;
            if (cb == null) return;

            SharedParamItem clickedItem = cb.DataContext as SharedParamItem;
            if (clickedItem == null) return;

            string selectedValue = (cb.SelectedItem as ComboBoxItem)?.Content as string;
            if (!string.IsNullOrEmpty(selectedValue))
            {
                clickedItem.BindingGroupName = selectedValue;

                var targetRows = GetTargetStagedParams(clickedItem);
                foreach (var item in targetRows)
                {
                    item.BindingGroupName = selectedValue;
                }
            }
        }

        // BATCH EDIT OPTIONS IN RIGHT SIDEBAR
        private void SetSelectedInstance_Click(object sender, RoutedEventArgs e)
        {
            var targetRows = GetTargetStagedParams();
            foreach (var item in targetRows)
            {
                item.IsInstance = true;
            }
            ProgressStatusTextBlock.Text = $"Привязка 'Экземпляр' назначена для {targetRows.Count} параметров.";
        }

        private void SetSelectedType_Click(object sender, RoutedEventArgs e)
        {
            var targetRows = GetTargetStagedParams();
            foreach (var item in targetRows)
            {
                item.IsInstance = false;
            }
            ProgressStatusTextBlock.Text = $"Привязка 'Типоразмер' назначена для {targetRows.Count} параметров.";
        }

        private void ApplyBatchGroup_Click(object sender, RoutedEventArgs e)
        {
            if (BatchGroupComboBox.SelectedValue is BuiltInParameterGroup targetGroup)
            {
                var targetRows = GetTargetStagedParams();
                foreach (var item in targetRows)
                {
                    item.BindingGroup = targetGroup;
                }
                ProgressStatusTextBlock.Text = $"Группа в свойствах назначена для {targetRows.Count} параметров.";
            }
        }

        private void ApplyBatchValue_Click(object sender, RoutedEventArgs e)
        {
            string val = BatchDefaultValueTextBox.Text;
            var targetRows = GetTargetStagedParams();
            foreach (var item in targetRows)
            {
                item.DefaultValue = val;
            }
            ProgressStatusTextBlock.Text = $"Значение / формула назначена для {targetRows.Count} параметров.";
        }

        private void UpdateStagedParamsCount()
        {
            int selected = _stagedParams.Count(p => p.IsSelected);
            StagedParamsCountTextBlock.Text = $"Подготовлено к записи: {selected} из {_stagedParams.Count} параметров";
        }

        // Categories Filtering
        private void FilterCategories()
        {
            if (_doc == null || _doc.IsFamilyDocument) return;

            string search = CategorySearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";
            var query = _allCategories.AsEnumerable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => c.Name.ToLowerInvariant().Contains(search));
            }

            _filteredCategories = new ObservableCollection<CategoryItem>(query);
            CategoriesListBox.ItemsSource = _filteredCategories;

            UpdateSelectedCategoriesCount();
        }

        private void CategorySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCategories();
        }

        private void CategoryCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            if (cb == null) return;

            CategoryItem clickedItem = cb.DataContext as CategoryItem;
            if (clickedItem == null) return;

            bool isChecked = cb.IsChecked == true;
            clickedItem.IsSelected = isChecked;

            var selectedItems = CategoriesListBox.SelectedItems.Cast<CategoryItem>().ToList();
            if (selectedItems.Contains(clickedItem) && selectedItems.Count > 1)
            {
                foreach (var item in selectedItems)
                {
                    item.IsSelected = isChecked;
                }
            }

            UpdateSelectedCategoriesCount();
        }

        private void SelectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _filteredCategories) c.IsSelected = true;
            UpdateSelectedCategoriesCount();
        }

        private void DeselectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _allCategories) c.IsSelected = false;
            UpdateSelectedCategoriesCount();
        }

        private void PresetMep_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllCategories_Click(sender, e);
            var mepBuiltIns = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_FlexDuctCurves,
                BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_DuctInsulations,
                BuiltInCategory.OST_PipeInsulations
            };

            foreach (var c in _allCategories)
            {
                if (mepBuiltIns.Contains(c.BuiltInCat)) c.IsSelected = true;
            }
            UpdateSelectedCategoriesCount();
        }

        private void PresetElec_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllCategories_Click(sender, e);
            var elecBuiltIns = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_ConduitFitting, BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_FireAlarmDevices
            };

            foreach (var c in _allCategories)
            {
                if (elecBuiltIns.Contains(c.BuiltInCat)) c.IsSelected = true;
            }
            UpdateSelectedCategoriesCount();
        }

        private void PresetArch_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllCategories_Click(sender, e);
            var archBuiltIns = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Stairs
            };

            foreach (var c in _allCategories)
            {
                if (archBuiltIns.Contains(c.BuiltInCat)) c.IsSelected = true;
            }
            UpdateSelectedCategoriesCount();
        }

        private void UpdateSelectedCategoriesCount()
        {
            if (_doc == null || _doc.IsFamilyDocument) return;

            int selected = _allCategories.Count(c => c.IsSelected);
            SelectedCategoriesCountTextBlock.Text = $"Категорий выбрано: {selected} из {_allCategories.Count}";
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            var selectedParams = _stagedParams.Where(p => p.IsSelected).ToList();
            if (selectedParams.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, подготовьте хотя бы один параметр в таблице к записи.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedCategories = _allCategories.Where(c => c.IsSelected).ToList();
            if (!_doc.IsFamilyDocument && selectedCategories.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы одну категорию проекта.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fopPath = FopFilePathTextBox.Text;

            try
            {
                ExecuteButton.IsEnabled = false;
                ProgressStatusTextBlock.Text = "Запись подготовленных параметров в модель...";

                var (added, updated) = BatchParamsEngine.ExecuteBatchAddParameters(
                    _doc,
                    fopPath,
                    selectedParams,
                    selectedCategories,
                    (pct, status) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            BatchProgressBar.Value = pct;
                            ProgressStatusTextBlock.Text = status;
                        });
                    });

                ProgressStatusTextBlock.Text = "Запись параметров успешно завершена!";
                BatchProgressBar.Value = 100;

                string targetDocStr = _doc.IsFamilyDocument ? "в семейство (.rfa)" : "в проект (.rvt)";

                MessageBox.Show(
                    this,
                    $"Пакетная запись параметров {targetDocStr} успешно завершена!\n\n" +
                    $"• Обработано параметров: {selectedParams.Count}\n" +
                    (!_doc.IsFamilyDocument ? $"• Назначено категорий проекта: {selectedCategories.Count}\n" : "") +
                    $"• Добавлено новых параметров: {added}\n" +
                    $"• Обновлено существующих: {updated}",
                    "BIMBCC | Результат пакетной записи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка добавления параметров:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressStatusTextBlock.Text = "Ошибка выполнения.";
            }
            finally
            {
                ExecuteButton.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
