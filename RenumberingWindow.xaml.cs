using System;
using System.Windows;

namespace BCCPlugIn
{
    public partial class RenumberingWindow : Window
    {
        public string Prefix => PrefixTextBox.Text.Trim();
        public int StartNum { get; private set; } = 1;

        public RenumberingWindow()
        {
            InitializeComponent();
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(StartNumTextBox.Text.Trim(), out int start) || start < 0)
            {
                MessageBox.Show("Пожалуйста, введите корректный начальный номер.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartNum = start;
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
