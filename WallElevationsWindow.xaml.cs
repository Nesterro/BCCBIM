using System;
using System.Windows;

namespace BCCPlugIn
{
    public partial class WallElevationsWindow : Window
    {
        public bool CreateNorth => NorthCheckBox.IsChecked ?? true;
        public bool CreateEast => EastCheckBox.IsChecked ?? true;
        public bool CreateSouth => SouthCheckBox.IsChecked ?? true;
        public bool CreateWest => WestCheckBox.IsChecked ?? true;

        public WallElevationsWindow()
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
