using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public partial class ViewIn3DWindow : Window
    {
        private readonly UIDocument _uidoc;
        public double PaddingMm => PaddingSlider.Value;
        public bool ShowSectionBox => ShowSectionBoxCheckBox.IsChecked ?? true;
        public ICollection<ElementId> PickedElementIds { get; private set; }

        public ViewIn3DWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
        }

        private void PickElementsButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                IList<Reference> refs = _uidoc.Selection.PickObjects(ObjectType.Element, "Выберите элементы в модели для 3D подрезки");
                if (refs != null && refs.Count > 0)
                {
                    PickedElementIds = new List<ElementId>();
                    foreach (Reference r in refs) PickedElementIds.Add(r.ElementId);
                }
            }
            catch { }
            Show();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
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
