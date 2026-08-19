using System;
using System.Windows;

namespace BCCPlugIn
{
    public partial class PlanDimensionsWindow : Window
    {
        public bool DimWalls => DimensionWallsCheckBox.IsChecked ?? true;
        public bool DimOpenings => DimensionOpeningsCheckBox.IsChecked ?? true;
        public bool DimGrids => DimensionGridsCheckBox.IsChecked ?? true;

        public PlanDimensionsWindow()
        {
            InitializeComponent();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
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
