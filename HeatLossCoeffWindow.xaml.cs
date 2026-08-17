using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BCCPlugIn
{
    public partial class HeatLossCoeffWindow : Window
    {
        public List<HeatLossCoeffItem> Items { get; private set; }

        public HeatLossCoeffWindow(List<HeatLossCoeffItem> items)
        {
            InitializeComponent();
            SetWindowIcon();

            Items = items ?? new List<HeatLossCoeffItem>();
            CoeffDataGrid.ItemsSource = Items;
        }

        private void SetWindowIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(
                    "BCCPlugIn.Resources.heat_loss_icon.png"))
                {
                    if (stream != null)
                    {
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                            stream,
                            System.Windows.Media.Imaging.BitmapCreateOptions.None,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            // Validate items
            foreach (var item in Items)
            {
                if (item.CoeffK <= 0)
                {
                    MessageBox.Show(this,
                        $"Коэффициент теплопередачи k для конструкции «{item.Code}» должен быть больше 0.",
                        "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
