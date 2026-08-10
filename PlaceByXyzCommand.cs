using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceByXyzCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            try
            {
                PlaceByXyzWindow window = new PlaceByXyzWindow(doc);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    var symbol = window.SelectedSymbol;
                    var level = window.SelectedLevel;
                    var points = window.PointsToPlace;
                    string targetParamName = window.TargetParamComboBox.SelectedItem as string;

                    if (symbol == null || level == null || points == null || points.Count == 0)
                    {
                        return Result.Cancelled;
                    }

                    // Open Progress window
                    ProgressWindow progress = new ProgressWindow();
                    WindowInteropHelper progressHelper = new WindowInteropHelper(progress);
                    progressHelper.Owner = uiapp.MainWindowHandle;
                    progress.Show();

                    int total = points.Count;
                    int successCount = 0;

                    using (Transaction trans = new Transaction(doc, "BIMBCC Расстановка по XYZ"))
                    {
                        trans.Start();

                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                            doc.Regenerate();
                        }

                        for (int i = 0; i < total; i++)
                        {
                            var pt = points[i];
                            double pct = ((double)(i + 1) / total) * 100;
                            progress.UpdateProgress($"Расстановка {i + 1} из {total}: (X:{pt.X:F0}, Y:{pt.Y:F0}, Z:{pt.Z:F0})...", pct);

                            // Convert mm to feet for Revit internal API units
                            XYZ location = new XYZ(
                                UnitUtils.ConvertToInternalUnits(pt.X, MillimeterUnitType),
                                UnitUtils.ConvertToInternalUnits(pt.Y, MillimeterUnitType),
                                UnitUtils.ConvertToInternalUnits(pt.Z, MillimeterUnitType)
                            );

                            FamilyInstance instance = doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.NonStructural);
                            if (instance != null)
                            {
                                successCount++;

                                // Write mark parameter if specified
                                if (!string.IsNullOrEmpty(pt.Mark) && !string.IsNullOrEmpty(targetParamName))
                                {
                                    Parameter p = instance.LookupParameter(targetParamName);
                                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                                    {
                                        p.Set(pt.Mark);
                                    }
                                }
                            }
                        }

                        trans.Commit();
                    }

                    progress.Close();

                    MessageBox.Show(
                        $"Успешно расставлено {successCount} из {total} элементов по координатам!",
                        "BIMBCC | Расстановка по XYZ",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при расстановке:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }

        private static ForgeTypeId MillimeterUnitType => UnitTypeId.Millimeters;
    }
}
