using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class ColorizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            try
            {
                Document doc = uidoc.Document;
                View activeView = uidoc.ActiveView;

                ColorizerWindow win = new ColorizerWindow(doc, activeView);
                if (win.ShowDialog() == true)
                {
                    using (Transaction t = new Transaction(doc, "BIMBCC: Временная раскраска элементов"))
                    {
                        t.Start();

                        List<Element> visibleElements = new FilteredElementCollector(doc, activeView.Id)
                            .WhereElementIsNotElementType()
                            .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                            .ToList();

                        if (win.IsResetRequested)
                        {
                            OverrideGraphicSettings cleanSettings = new OverrideGraphicSettings();
                            foreach (Element e in visibleElements)
                            {
                                activeView.SetElementOverrides(e.Id, cleanSettings);
                            }
                            t.Commit();
                            TaskDialog.Show("Раскраска элементов", "Переопределения графики успешно сброшены.");
                            return Result.Succeeded;
                        }

                        // Generate palette of colors
                        var groups = visibleElements
                            .GroupBy(e => {
                                Parameter p = e.LookupParameter(win.SelectedParameterName);
                                return p != null && p.HasValue ? (p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString()) : "<Не задано>";
                            })
                            .ToList();

                        Color[] palette = GetColorPalette(groups.Count);
                        int colorIdx = 0;

                        FillPatternElement solidPattern = new FilteredElementCollector(doc)
                            .OfClass(typeof(FillPatternElement))
                            .Cast<FillPatternElement>()
                            .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                        foreach (var group in groups)
                        {
                            Color c = palette[colorIdx % palette.Length];
                            colorIdx++;

                            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(c);
                            ogs.SetSurfaceForegroundPatternColor(c);
                            if (solidPattern != null) ogs.SetSurfaceForegroundPatternId(solidPattern.Id);

                            foreach (Element elem in group)
                            {
                                activeView.SetElementOverrides(elem.Id, ogs);
                            }
                        }

                        t.Commit();
                        TaskDialog.Show("Раскраска элементов", $"Элементы активного вида успешно раскрашены в {groups.Count} цветов по параметру '{win.SelectedParameterName}'.");
                    }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private Color[] GetColorPalette(int count)
        {
            Color[] preset = new Color[]
            {
                new Color(230, 25, 75), new Color(60, 180, 75), new Color(255, 225, 25),
                new Color(0, 130, 200), new Color(245, 130, 48), new Color(145, 30, 180),
                new Color(70, 240, 240), new Color(240, 50, 230), new Color(210, 245, 60),
                new Color(250, 190, 212), new Color(0, 128, 128), new Color(220, 190, 255)
            };
            return preset;
        }
    }
}
