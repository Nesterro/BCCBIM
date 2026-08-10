using System;
using System.Collections.Generic;
using System.IO;
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
    public class OpeningsCommand : IExternalCommand
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
                // Find all generic models family symbols to choose from
                List<FamilySymbol> openingSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Category != null && s.Category.Id == new ElementId(BuiltInCategory.OST_GenericModel))
                    .ToList();

                if (openingSymbols.Count == 0)
                {
                    MessageBox.Show("В проекте не найдено семейств категории 'Обобщенные модели' для задания отверстий.\nПожалуйста, загрузите подходящие семейства отверстий в проект.", "Задание на отверстия", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // Show WPF window
                OpeningsWindow window = new OpeningsWindow(openingSymbols);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    FamilySymbol selectedWallSymbol = window.SelectedWallSymbol;
                    FamilySymbol selectedFloorSymbol = window.SelectedFloorSymbol;
                    double clearanceMm = window.ClearanceMm;
                    string widthParamName = window.WidthParam;
                    string heightParamName = window.HeightParam;
                    string depthParamName = window.DepthParam;

                    // Collect MEP elements (Pipes, Ducts, Cable Trays)
                    List<MEPCurve> mepElements = new List<MEPCurve>();
                    
                    var pipes = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType()
                        .Cast<MEPCurve>();
                    mepElements.AddRange(pipes);

                    var ducts = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType()
                        .Cast<MEPCurve>();
                    mepElements.AddRange(ducts);

                    var trays = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_CableTray)
                        .WhereElementIsNotElementType()
                        .Cast<MEPCurve>();
                    mepElements.AddRange(trays);

                    if (mepElements.Count == 0)
                    {
                        MessageBox.Show("В проекте не найдено инженерных сетей (труб, воздуховодов или лотков).", "Задание на отверстия", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Result.Succeeded;
                    }

                    // Collect Host elements (Walls, Floors)
                    List<ElementId> hostCategoryIds = new List<ElementId>();
                    if (window.IntersectWalls)
                        hostCategoryIds.Add(new ElementId(BuiltInCategory.OST_Walls));
                    if (window.IntersectFloors)
                        hostCategoryIds.Add(new ElementId(BuiltInCategory.OST_Floors));

                    // Get all host elements
                    List<Element> hostElements = new List<Element>();
                    foreach (var catId in hostCategoryIds)
                    {
                        var elementsOfCategory = new FilteredElementCollector(doc)
                            .OfCategoryId(catId)
                            .WhereElementIsNotElementType()
                            .ToList();
                        hostElements.AddRange(elementsOfCategory);
                    }

                    if (hostElements.Count == 0)
                    {
                        MessageBox.Show("В проекте не найдено конструкций (стен или перекрытий) для пересечения.", "Задание на отверстия", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Result.Succeeded;
                    }

                    List<ElementId> hostIds = hostElements.Select(h => h.Id).ToList();
                    int placedCount = 0;

                    using (Transaction trans = new Transaction(doc, "BIMBCC Размещение отверстий"))
                    {
                        trans.Start();

                        // Activate family symbols to prevent exceptions
                        if (selectedWallSymbol != null && !selectedWallSymbol.IsActive)
                        {
                            selectedWallSymbol.Activate();
                        }
                        if (selectedFloorSymbol != null && !selectedFloorSymbol.IsActive)
                        {
                            selectedFloorSymbol.Activate();
                        }
                        doc.Regenerate();

                        foreach (MEPCurve mepCurve in mepElements)
                        {
                            LocationCurve locCurve = mepCurve.Location as LocationCurve;
                            if (locCurve == null || locCurve.Curve == null) continue;

                            Curve mepCurveGeometry = locCurve.Curve;

                            // BoundingBox check to quickly filter potential collision hosts
                            BoundingBoxXYZ bbox = mepCurve.get_BoundingBox(null);
                            if (bbox == null) continue;

                            Outline outline = new Outline(bbox.Min, bbox.Max);
                            BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

                            // Find colliding host elements
                            FilteredElementCollector collidingHostsColl = new FilteredElementCollector(doc, hostIds);
                            collidingHostsColl.WherePasses(bboxFilter);

                            foreach (Element hostElement in collidingHostsColl)
                            {
                                List<Solid> solids = GetElementSolids(hostElement);
                                foreach (Solid solid in solids)
                                {
                                    SolidCurveIntersectionOptions intersectionOptions = new SolidCurveIntersectionOptions();
                                    SolidCurveIntersection intersection = solid.IntersectWithCurve(mepCurveGeometry, intersectionOptions);
                                    if (intersection != null && intersection.SegmentCount > 0)
                                    {
                                        for (int i = 0; i < intersection.SegmentCount; i++)
                                        {
                                            Curve segment = intersection.GetCurveSegment(i);
                                            XYZ point = (segment.GetEndPoint(0) + segment.GetEndPoint(1)) / 2.0;

                                            // Determine which symbol to place
                                            FamilySymbol symbolToPlace = null;
                                            if (hostElement is Wall)
                                                symbolToPlace = selectedWallSymbol;
                                            else
                                                symbolToPlace = selectedFloorSymbol;

                                            if (symbolToPlace == null) continue;

                                            // Place instance
                                            FamilyInstance opening = doc.Create.NewFamilyInstance(
                                                point, 
                                                symbolToPlace, 
                                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                                            );

                                            // Determine MEP sizes
                                            double mepWidth = 0;
                                            double mepHeight = 0;
                                            GetMepDimensions(mepCurve, out mepWidth, out mepHeight);

                                            // Total dimensions with clearances
                                            double finalWidth = mepWidth + (2 * clearanceMm);
                                            double finalHeight = mepHeight + (2 * clearanceMm);
                                            double finalDepth = segment.Length * 304.8; // Host element thickness at crossing path

                                            // Write parameters to opening instance
                                            SetParameterValue(opening, widthParamName, finalWidth);
                                            SetParameterValue(opening, heightParamName, finalHeight);
                                            SetParameterValue(opening, depthParamName, finalDepth);

                                            // Orient opening along the host Wall angle (if Wall)
                                            if (hostElement is Wall wall)
                                            {
                                                try
                                                {
                                                    XYZ normal = wall.Orientation;
                                                    double angle = Math.Atan2(normal.Y, normal.X) - Math.PI / 2.0;
                                                    if (Math.Abs(angle) > 0.001)
                                                    {
                                                        Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                                        ElementTransformUtils.RotateElement(doc, opening.Id, axis, angle);
                                                    }
                                                }
                                                catch { }
                                            }

                                            placedCount++;
                                        }
                                    }
                                }
                            }
                        }

                        trans.Commit();
                    }

                    TaskDialog.Show("BIMBCC | Задание на отверстия", $"Расстановка заданий на отверстия успешно завершена!\nРазмещено отверстий: {placedCount}");
                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при расстановке отверстий:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }

        private List<Solid> GetElementSolids(Element element)
        {
            List<Solid> solids = new List<Solid>();
            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true
            };
            GeometryElement geomElem = element.get_Geometry(options);
            if (geomElem != null)
            {
                foreach (GeometryObject geomObj in geomElem)
                {
                    if (geomObj is Solid solid && solid.Volume > 0.0001)
                    {
                        solids.Add(solid);
                    }
                    else if (geomObj is GeometryInstance geomInst)
                    {
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > 0.0001)
                            {
                                solids.Add(instSolid);
                            }
                        }
                    }
                }
            }
            return solids;
        }

        private void GetMepDimensions(MEPCurve curve, out double width, out double height)
        {
            width = 0;
            height = 0;

            if (curve is Pipe pipe)
            {
                double diameter = 0;
                Parameter diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
                if (diameterParam != null)
                {
                    diameter = diameterParam.AsDouble();
                }
                else
                {
                    diameter = pipe.Diameter;
                }
                width = diameter * 304.8;
                height = diameter * 304.8;
            }
            else if (curve is Duct duct)
            {
                Parameter widthParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                Parameter heightParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                Parameter diameterParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);

                if (widthParam != null && heightParam != null)
                {
                    width = widthParam.AsDouble() * 304.8;
                    height = heightParam.AsDouble() * 304.8;
                }
                else if (diameterParam != null)
                {
                    double diameter = diameterParam.AsDouble() * 304.8;
                    width = diameter;
                    height = diameter;
                }
            }
            else if (curve is CableTray tray)
            {
                Parameter widthParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                Parameter heightParam = tray.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM); // or RBS_CABLETRAY_HEIGHT_PARAM

                if (widthParam == null) widthParam = tray.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                
                double w = widthParam != null ? widthParam.AsDouble() : 0;
                double h = heightParam != null ? heightParam.AsDouble() : 0;

                width = w * 304.8;
                height = h * 304.8;
            }
        }

        private void SetParameterValue(Element elem, string paramName, double valMm)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            double valFeet = valMm / 304.8;
            
            if (param.StorageType == StorageType.Double)
            {
                param.Set(valFeet);
            }
            else if (param.StorageType == StorageType.Integer)
            {
                param.Set((int)valMm);
            }
            else if (param.StorageType == StorageType.String)
            {
                param.Set(valMm.ToString("F0"));
            }
        }
    }
}
