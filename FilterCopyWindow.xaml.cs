using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class FilterCopyWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly FilterCopyEngine _engine;

        private List<SourceViewItem> _sourceViews;
        private List<FilterItem> _filters;
        private List<TargetViewItem> _targetViews;

        private ICollectionView _filtersCollectionView;
        private ICollectionView _targetsCollectionView;

        public FilterCopyWindow(Document doc, View activeView)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView;
            _engine = new FilterCopyEngine(_doc);

            LoadSourceViews();
        }

        private void LoadSourceViews()
        {
            _sourceViews = _engine.GetSourceViews();
            SourceViewComboBox.ItemsSource = _sourceViews;

            if (_sourceViews.Count == 0)
            {
                StatusTextBlock.Text = "В проекте не найдено видов или шаблонов с примененными фильтрами.";
                CopyButton.IsEnabled = false;
                return;
            }

            // Пытаемся выбрать активный вид или его шаблон
            SourceViewItem activeItem = null;
            if (_activeView != null)
            {
                activeItem = _sourceViews.FirstOrDefault(sv => sv.ViewId == _activeView.Id);
                if (activeItem == null && _activeView.ViewTemplateId != ElementId.InvalidElementId)
                {
                    activeItem = _sourceViews.FirstOrDefault(sv => sv.ViewId == _activeView.ViewTemplateId);
                }
            }

            if (activeItem != null)
            {
                SourceViewComboBox.SelectedItem = activeItem;
            }
            else
            {
                SourceViewComboBox.SelectedIndex = 0;
            }
        }

        private void SourceViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SourceViewItem selectedSource = SourceViewComboBox.SelectedItem as SourceViewItem;
            if (selectedSource == null) return;

            // Загрузить фильтры выбранного вида
            _filters = _engine.GetViewFilters(selectedSource.ViewId);
            if (_filters != null)
            {
                foreach (var f in _filters)
                {
                    f.PropertyChanged -= Item_PropertyChanged;
                    f.PropertyChanged += Item_PropertyChanged;
                }
            }
            FiltersDataGrid.ItemsSource = _filters;
            _filtersCollectionView = CollectionViewSource.GetDefaultView(_filters);
            if (_filtersCollectionView != null)
            {
                _filtersCollectionView.Filter = FilterSearchPredicate;
            }

            // Загрузить целевые виды (исключая выбранный исходный)
            _targetViews = _engine.GetTargetViews(selectedSource.ViewId);
            if (_targetViews != null)
            {
                foreach (var t in _targetViews)
                {
                    t.PropertyChanged -= Item_PropertyChanged;
                    t.PropertyChanged += Item_PropertyChanged;
                }
            }
            TargetsDataGrid.ItemsSource = _targetViews;
            _targetsCollectionView = CollectionViewSource.GetDefaultView(_targetViews);
            if (_targetsCollectionView != null)
            {
                _targetsCollectionView.Filter = TargetSearchPredicate;
            }

            UpdateStatusText();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FilterItem.IsSelected) || e.PropertyName == nameof(TargetViewItem.IsSelected))
            {
                UpdateStatusText();
            }
        }

        #region Multi-Row CheckBox Toggling

        private void FilterCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is FilterItem currentItem)
            {
                bool newState = cb.IsChecked ?? false;
                var selectedRows = FiltersDataGrid.SelectedItems.Cast<FilterItem>().ToList();

                if (selectedRows.Count > 1 && selectedRows.Contains(currentItem))
                {
                    foreach (var item in selectedRows)
                    {
                        item.IsSelected = newState;
                    }
                }
                else
                {
                    currentItem.IsSelected = newState;
                }

                UpdateStatusText();
            }
        }

        private void TargetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is TargetViewItem currentItem)
            {
                bool newState = cb.IsChecked ?? false;
                var selectedRows = TargetsDataGrid.SelectedItems.Cast<TargetViewItem>().ToList();

                if (selectedRows.Count > 1 && selectedRows.Contains(currentItem))
                {
                    foreach (var item in selectedRows)
                    {
                        item.IsSelected = newState;
                    }
                }
                else
                {
                    currentItem.IsSelected = newState;
                }

                UpdateStatusText();
            }
        }

        #endregion

        #region Live Search Filters & Targets

        private bool FilterSearchPredicate(object item)
        {
            if (item is FilterItem filter)
            {
                string search = FilterSearchTextBox.Text.Trim();
                if (string.IsNullOrEmpty(search)) return true;

                return filter.FilterName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       filter.OverridesSummary.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        private bool TargetSearchPredicate(object item)
        {
            if (item is TargetViewItem target)
            {
                // Фильтр вида по чекбоксу шаблонов
                bool includeViewsWithTemplate = IncludeViewsWithTemplateCheckBox.IsChecked ?? true;
                if (!includeViewsWithTemplate && target.HasTemplateApplied && !target.IsTemplate)
                {
                    return false;
                }

                // Фильтр по выбранной категории видов
                if (ViewTypeCategoryComboBox.SelectedItem is ComboBoxItem catItem)
                {
                    string category = catItem.Content.ToString();
                    if (category == "Планы этажей" && target.ViewType != ViewType.FloorPlan && target.ViewType != ViewType.EngineeringPlan && target.ViewType != ViewType.AreaPlan) return false;
                    if (category == "3D виды" && target.ViewType != ViewType.ThreeD) return false;
                    if (category == "Разрезы / Фасады" && target.ViewType != ViewType.Section && target.ViewType != ViewType.Elevation) return false;
                    if (category == "Шаблоны видов" && !target.IsTemplate) return false;
                }

                // Фильтр по поисковой строке
                string search = TargetSearchTextBox.Text.Trim();
                if (string.IsNullOrEmpty(search)) return true;

                return target.ViewName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       target.ViewTypeGroup.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       target.TemplateName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        private void FilterSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filtersCollectionView?.Refresh();
        }

        private void TargetSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _targetsCollectionView?.Refresh();
        }

        private void ViewTypeCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _targetsCollectionView?.Refresh();
        }

        private void IncludeViewsWithTemplateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _targetsCollectionView?.Refresh();
        }

        #endregion

        #region Selection Handlers

        private void SelectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            if (_filters == null) return;
            foreach (var f in _filters) f.IsSelected = true;
            UpdateStatusText();
        }

        private void DeselectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            if (_filters == null) return;
            foreach (var f in _filters) f.IsSelected = false;
            UpdateStatusText();
        }

        private void SelectVisibleTargets_Click(object sender, RoutedEventArgs e)
        {
            if (_targetsCollectionView == null) return;
            foreach (var item in _targetsCollectionView)
            {
                if (item is TargetViewItem target)
                {
                    target.IsSelected = true;
                }
            }
            UpdateStatusText();
        }

        private void DeselectAllTargets_Click(object sender, RoutedEventArgs e)
        {
            if (_targetViews == null) return;
            foreach (var t in _targetViews) t.IsSelected = false;
            UpdateStatusText();
        }

        #endregion

        private void UpdateStatusText()
        {
            int selectedFilterCount = _filters != null ? _filters.Count(f => f.IsSelected) : 0;
            int selectedTargetCount = _targetViews != null ? _targetViews.Count(t => t.IsSelected) : 0;

            StatusTextBlock.Text = $"Выбрано фильтров: {selectedFilterCount} | Целевых видов: {selectedTargetCount}";
            CopyButton.IsEnabled = (selectedFilterCount > 0 && selectedTargetCount > 0);
            CopyButton.Content = selectedTargetCount > 0
                ? $"Скопировать фильтры ({selectedFilterCount} на {selectedTargetCount} в.)"
                : "Скопировать фильтры";
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            SourceViewItem selectedSource = SourceViewComboBox.SelectedItem as SourceViewItem;
            if (selectedSource == null) return;

            List<FilterItem> selectedFilters = _filters?.Where(f => f.IsSelected).ToList();
            List<TargetViewItem> selectedTargets = _targetViews?.Where(t => t.IsSelected).ToList();

            if (selectedFilters == null || selectedFilters.Count == 0 || selectedTargets == null || selectedTargets.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выделите хотя бы один фильтр и один целевой вид.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilterCopyOptions options = new FilterCopyOptions
            {
                OverwriteExistingFilters = OverwriteExistingCheckBox.IsChecked ?? true,
                CopyToAppliedTemplates = CopyToAppliedTemplatesCheckBox.IsChecked ?? true,
                IncludeViewsWithTemplate = IncludeViewsWithTemplateCheckBox.IsChecked ?? true,
                CopyGraphicOverrides = CopyGraphicOverridesCheckBox.IsChecked ?? true,
                CopyVisibility = CopyVisibilityCheckBox.IsChecked ?? true
            };

            try
            {
                FilterCopyResult result = _engine.ExecuteCopy(selectedSource.ViewId, selectedFilters, selectedTargets, options);

                if (result.Success)
                {
                    string warningText = result.Warnings.Count > 0
                        ? $"\n\nПредупреждения ({result.Warnings.Count}):\n" + string.Join("\n", result.Warnings.Take(5))
                        : "";

                    MessageBox.Show($"{result.Message}{warningText}", "Успешное копирование", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(result.Message, "Ошибка копирования", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка при копировании фильтров:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
