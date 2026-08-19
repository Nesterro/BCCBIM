using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class LevelCopyItem : INotifyPropertyChanged
    {
        public Level Level { get; set; }
        public string Name => Level != null ? Level.Name : "Уровень";
        public string FormattedElevation => Level != null ? $"{(Level.Elevation * 304.8 / 1000.0):F3}" : "0.000";

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class CopyElementsWindow : Window
    {
        private readonly Document _doc;
        private List<LevelCopyItem> _levels;

        public List<Level> SelectedLevels => _levels.Where(l => l.IsSelected).Select(l => l.Level).ToList();

        public CopyElementsWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadLevels();
        }

        private void LoadLevels()
        {
            List<Level> levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            _levels = levels.Select(l => new LevelCopyItem { Level = l, IsSelected = false }).ToList();
            LevelsDataGrid.ItemsSource = _levels;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLevels.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы один целевой уровень.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
