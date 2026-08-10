using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class ScheduleItemVM
    {
        public ViewSchedule Schedule { get; set; }
        public string Name => Schedule.Name;
        public bool IsSelected { get; set; }
    }

    public partial class SchedulePackWindow : Window
    {
        private readonly List<ScheduleItemVM> _allSchedules;
        private List<ScheduleItemVM> _filteredSchedules;

        public List<ViewSchedule> SelectedSchedules => _allSchedules.Where(s => s.IsSelected).Select(s => s.Schedule).ToList();
        public string SelectedFolderPath => FolderTextBox.Text;
        public bool ExportAsCsv => CsvRadioButton.IsChecked == true;

        public SchedulePackWindow(List<ViewSchedule> schedules, string defaultFolder)
        {
            InitializeComponent();

            FolderTextBox.Text = defaultFolder;
            _allSchedules = schedules.Select(s => new ScheduleItemVM { Schedule = s, IsSelected = true }).ToList();
            _filteredSchedules = new List<ScheduleItemVM>(_allSchedules);

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(query))
            {
                _filteredSchedules = new List<ScheduleItemVM>(_allSchedules);
            }
            else
            {
                _filteredSchedules = _allSchedules
                    .Where(s => s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            SchedulesListBox.ItemsSource = _filteredSchedules;
            UpdateCount();
        }

        private void UpdateCount()
        {
            int selectedCount = _allSchedules.Count(s => s.IsSelected);
            SelectedCountText.Text = $" (выбрано: {selectedCount} из {_allSchedules.Count})";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateCount();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredSchedules) item.IsSelected = true;
            SchedulesListBox.Items.Refresh();
            UpdateCount();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredSchedules) item.IsSelected = false;
            SchedulesListBox.Items.Refresh();
            UpdateCount();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для сохранения спецификаций";
                dialog.SelectedPath = FolderTextBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FolderTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSchedules.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы одну спецификацию.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedFolderPath) || !Directory.Exists(SelectedFolderPath))
            {
                MessageBox.Show("Пожалуйста, выберите существующую папку для сохранения.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
