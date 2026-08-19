using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class RoomFinishingWindow : Window
    {
        private readonly Document _doc;
        public WallType SelectedWallType => (WallType)WallTypeComboBox.SelectedItem;
        public double FinishHeightMm { get; private set; } = 3000;

        public RoomFinishingWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadWallTypes();
        }

        private void LoadWallTypes()
        {
            List<WallType> wallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(w => w.Name)
                .ToList();

            WallTypeComboBox.ItemsSource = wallTypes;
            WallTypeComboBox.DisplayMemberPath = "Name";
            if (wallTypes.Count > 0) WallTypeComboBox.SelectedIndex = 0;
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWallType == null)
            {
                MessageBox.Show("Пожалуйста, выберите тип стены.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(HeightTextBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double h) || h <= 0)
            {
                MessageBox.Show("Пожалуйста, введите корректную высоту отделки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FinishHeightMm = h;
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
