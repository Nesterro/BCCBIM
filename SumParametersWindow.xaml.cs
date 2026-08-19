using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class ParameterStatItem
    {
        public string ParameterName { get; set; }
        public double Sum { get; set; }
        public double Avg => Count > 0 ? Sum / Count : 0;
        public double Min { get; set; }
        public double Max { get; set; }
        public int Count { get; set; }
        public string UnitName { get; set; }

        public string FormattedSum => $"{Sum:F2} {UnitName}".Trim();
        public string FormattedAvg => $"{Avg:F2} {UnitName}".Trim();
        public string FormattedMin => $"{Min:F2} {UnitName}".Trim();
        public string FormattedMax => $"{Max:F2} {UnitName}".Trim();
    }

    public partial class SumParametersWindow : Window
    {
        public SumParametersWindow(Document doc, ICollection<ElementId> selectedIds)
        {
            InitializeComponent();
            CalculateStats(doc, selectedIds);
        }

        private void CalculateStats(Document doc, ICollection<ElementId> selectedIds)
        {
            if (selectedIds == null || selectedIds.Count == 0)
            {
                SelectedCountTextBlock.Text = "Выделено элементов: 0";
                return;
            }

            SelectedCountTextBlock.Text = $"Выделено элементов: {selectedIds.Count}";

            Dictionary<string, List<double>> paramValues = new Dictionary<string, List<double>>();

            foreach (ElementId id in selectedIds)
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;

                foreach (Parameter p in elem.Parameters)
                {
                    if (p == null || !p.HasValue) continue;

                    double val = 0;
                    bool isVal = false;

                    if (p.StorageType == StorageType.Double)
                    {
                        val = p.AsDouble();
                        // Convert internal feet/sqft/cuft to metric
                        BuiltInParameter bip = BuiltInParameter.INVALID;
                        try { bip = (BuiltInParameter)p.Id.Value; } catch { }
                        if (bip == BuiltInParameter.HOST_AREA_COMPUTED || bip == BuiltInParameter.ROOM_AREA) val *= 0.092903; // m2
                        else if (bip == BuiltInParameter.HOST_VOLUME_COMPUTED || bip == BuiltInParameter.ROOM_VOLUME) val *= 0.0283168; // m3
                        else if (bip == BuiltInParameter.CURVE_ELEM_LENGTH || bip == BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH) val *= 304.8 / 1000.0; // m
                        isVal = true;
                    }
                    else if (p.StorageType == StorageType.Integer)
                    {
                        val = p.AsInteger();
                        isVal = true;
                    }

                    if (isVal)
                    {
                        string pName = p.Definition != null ? p.Definition.Name : "Параметр";
                        if (!paramValues.ContainsKey(pName)) paramValues[pName] = new List<double>();
                        paramValues[pName].Add(val);
                    }
                }
            }

            List<ParameterStatItem> stats = new List<ParameterStatItem>();
            foreach (var kv in paramValues)
            {
                if (kv.Value.Count == 0) continue;
                stats.Add(new ParameterStatItem
                {
                    ParameterName = kv.Key,
                    Sum = kv.Value.Sum(),
                    Min = kv.Value.Min(),
                    Max = kv.Value.Max(),
                    Count = kv.Value.Count
                });
            }

            StatsDataGrid.ItemsSource = stats.OrderByDescending(s => s.Count).ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
