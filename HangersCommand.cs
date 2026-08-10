using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangersCommand : IExternalCommand
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
                // Find potential hanger family symbols (Generic Models, Pipe Accessories, Duct Accessories)
                List<FamilySymbol> hangerSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Category != null && 
                               (s.Category.Id == new ElementId(BuiltInCategory.OST_GenericModel) ||
                                s.Category.Id == new ElementId(BuiltInCategory.OST_PipeAccessory) ||
                                s.Category.Id == new ElementId(BuiltInCategory.OST_DuctAccessory)))
                    .ToList();

                if (hangerSymbols.Count == 0)
                {
                    MessageBox.Show("В проекте не найдено загруженных семейств в категориях 'Обобщенные модели', 'Арматура трубопроводов' или 'Арматура воздуховодов'.\nПожалуйста, загрузите семейство крепления в проект.", "Расстановка крепежа", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // Show WPF window
                HangersWindow window = new HangersWindow(hangerSymbols);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    FamilySymbol selectedSymbol = window.SelectedSymbol;
                    double spacingMm = window.SpacingMm;
                    double spacingFeet = spacingMm / 304.8;

                    // Collect target MEP elements based on selection
                    List<MEPCurve> mepElements = new List<MEPCurve>();

                    if (window.RunPipes)
                    {
                        var pipes = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_PipeCurves)
                            .WhereElementIsNotElementType()
                            .Cast<MEPCurve>();
                        mepElements.AddRange(pipes);
                    }

                    if (window.RunDucts)
                    {
                        var ducts = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_DuctCurves)
                            .WhereElementIsNotElementType()
                            .Cast<MEPCurve>();
                        mepElements.AddRange(ducts);
                    }

                    if (window.RunTrays)
                    {
                        var trays = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_CableTray)
                            .WhereElementIsNotElementType()
                            .Cast<MEPCurve>();
                        mepElements.AddRange(trays);
                    }

                    if (mepElements.Count == 0)
                    {
                        MessageBox.Show("В проекте не найдено элементов выбранных сетей.", "Расстановка крепежа", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Result.Succeeded;
                    }

                    int placedCount = 0;

                    using (Transaction trans = new Transaction(doc, "BIMBCC Расстановка крепежа"))
                    {
                        trans.Start();

                        // Activate family symbol to prevent exception
                        if (!selectedSymbol.IsActive)
                        {
                            selectedSymbol.Activate();
                            doc.Regenerate();
                        }

                        foreach (MEPCurve mepCurve in mepElements)
                        {
                            LocationCurve locCurve = mepCurve.Location as LocationCurve;
                            if (locCurve == null || locCurve.Curve == null) continue;

                            Curve curve = locCurve.Curve;
                            double length = curve.Length;

                            if (length <= spacingFeet) continue;

                            // Spacing logic: place along the curve length at spacing intervals
                            double currentLength = spacingFeet;
                            while (currentLength < length)
                            {
                                double t = currentLength / length; // Normalized parameter (0.0 to 1.0)
                                XYZ point = curve.Evaluate(t, true);

                                // Create family instance
                                FamilyInstance hanger = doc.Create.NewFamilyInstance(
                                    point, 
                                    selectedSymbol, 
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                                );

                                // Orient hanger to align with the curve direction
                                try
                                {
                                    Transform derivatives = curve.ComputeDerivatives(t, true);
                                    XYZ tangent = derivatives.BasisX.Normalize();
                                    // Project tangent onto XY plane to get rotation angle around Z axis
                                    double angle = Math.Atan2(tangent.Y, tangent.X);
                                    if (Math.Abs(angle) > 0.001)
                                    {
                                        Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                        ElementTransformUtils.RotateElement(doc, hanger.Id, axis, angle);
                                    }
                                }
                                catch
                                {
                                    // Ignore rotation errors (e.g. vertical elements or zero-length vectors)
                                }

                                placedCount++;
                                currentLength += spacingFeet;
                            }
                        }

                        trans.Commit();
                    }

                    TaskDialog.Show("BIMBCC | Расстановка крепежа", $"Расстановка успешно завершена!\nРазмещено креплений: {placedCount}");
                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при расстановке крепежа:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}
