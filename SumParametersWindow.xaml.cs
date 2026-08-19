using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class ParameterStatItem : INotifyPropertyChanged
    {
        public string ParameterName { get; set; }
        public double RawSum { get; set; }
        public int Count { get; set; }
        public string UnitName { get; set; }

        private double _coeff = 1.0;
        public double Coeff
        {
            get => _coeff;
            set
            {
                _coeff = value;
                OnPropertyChanged(nameof(Coeff));
                OnPropertyChanged(nameof(FormattedSumWithCoeff));
            }
        }

        public string FormattedSum => $"{RawSum:N2}";
        public string FormattedSumWithCoeff => $"{(RawSum * Coeff):N2}";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class SumParametersWindow : Window
    {
        private List<ParameterStatItem> _statsList;

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
            Dictionary<string, (double sum, string unit, int count)> paramData = new Dictionary<string, (double, string, int)>();

            foreach (ElementId id in selectedIds)
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;

                foreach (Parameter p in elem.Parameters)
                {
                    if (p == null || !p.HasValue) continue;

                    double val = 0;
                    string unit = "шт";
                    bool isVal = false;

                    if (p.StorageType == StorageType.Double)
                    {
                        val = p.AsDouble();
                        BuiltInParameter bip = BuiltInParameter.INVALID;
                        try { bip = (BuiltInParameter)p.Id.Value; } catch { }

                        if (bip == BuiltInParameter.HOST_AREA_COMPUTED || bip == BuiltInParameter.ROOM_AREA)
                        {
                            val *= 0.092903;
                            unit = "м²";
                        }
                        else if (bip == BuiltInParameter.HOST_VOLUME_COMPUTED || bip == BuiltInParameter.ROOM_VOLUME)
                        {
                            val *= 0.0283168;
                            unit = "м³";
                        }
                        else if (bip == BuiltInParameter.CURVE_ELEM_LENGTH || bip == BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH || bip == BuiltInParameter.WALL_USER_HEIGHT_PARAM)
                        {
                            val *= 304.8 / 1000.0;
                            unit = "м";
                        }
                        else
                        {
                            unit = "ед";
                        }
                        isVal = true;
                    }
                    else if (p.StorageType == StorageType.Integer)
                    {
                        val = p.AsInteger();
                        unit = "шт";
                        isVal = true;
                    }

                    if (isVal)
                    {
                        string pName = p.Definition != null ? p.Definition.Name : "Параметр";
                        if (!paramData.ContainsKey(pName)) paramData[pName] = (0, unit, 0);
                        var current = paramData[pName];
                        paramData[pName] = (current.sum + val, unit, current.count + 1);
                    }
                }
            }

            _statsList = new List<ParameterStatItem>();
            foreach (var kv in paramData)
            {
                _statsList.Add(new ParameterStatItem
                {
                    ParameterName = kv.Key,
                    RawSum = kv.Value.sum,
                    UnitName = kv.Value.unit,
                    Count = kv.Value.count,
                    Coeff = GetCurrentCoeff()
                });
            }

            StatsDataGrid.ItemsSource = _statsList.OrderByDescending(s => s.Count).ToList();
        }

        private double GetCurrentCoeff()
        {
            if (double.TryParse(CoeffTextBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double c))
            {
                return c > 0 ? c : 1.0;
            }
            return 1.0;
        }

        private void CoeffTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_statsList == null) return;
            double coeff = GetCurrentCoeff();
            foreach (var item in _statsList)
            {
                item.Coeff = coeff;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
