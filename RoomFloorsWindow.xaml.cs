using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class RoomFloorsWindow : Window
    {
        private readonly Document _doc;
        public FloorType SelectedFloorType => (FloorType)FloorTypeComboBox.SelectedItem;

        public RoomFloorsWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadFloorTypes();
        }

        private void LoadFloorTypes()
        {
            List<FloorType> floorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(f => f.Name)
                .ToList();

            FloorTypeComboBox.ItemsSource = floorTypes;
            FloorTypeComboBox.DisplayMemberPath = "Name";
            if (floorTypes.Count > 0) FloorTypeComboBox.SelectedIndex = 0;
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFloorType == null)
            {
                MessageBox.Show("Пожалуйста, выберите тип пола.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
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
