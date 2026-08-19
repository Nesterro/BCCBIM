using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class MaterialItem
    {
        public Material Material { get; set; }
        public string Name => Material != null ? Material.Name : "Материал";
        public string MaterialClass => Material != null ? Material.MaterialClass : "";
    }

    public partial class MaterialManagerWindow : Window
    {
        private readonly Document _doc;

        public MaterialManagerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadMaterials();
        }

        private void LoadMaterials()
        {
            List<Material> mats = new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(m => m.Name)
                .ToList();

            MaterialsDataGrid.ItemsSource = mats.Select(m => new MaterialItem { Material = m }).ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
