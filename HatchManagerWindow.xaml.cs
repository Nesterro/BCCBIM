using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class HatchItem
    {
        public FillPatternElement Element { get; set; }
        public string Name => Element != null ? Element.Name : "Штриховка";
        public string TargetType => Element != null ? Element.GetFillPattern().Target.ToString() : "";
    }

    public partial class HatchManagerWindow : Window
    {
        private readonly Document _doc;

        public HatchManagerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadHatches();
        }

        private void LoadHatches()
        {
            List<FillPatternElement> patterns = new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .OrderBy(p => p.Name)
                .ToList();

            HatchDataGrid.ItemsSource = patterns.Select(p => new HatchItem { Element = p }).ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
