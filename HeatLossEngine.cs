using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;

namespace BCCPlugIn
{
    public class HeatLossBoundaryItem
    {
        public ElementId SpaceId { get; set; }
        public string SpaceName { get; set; }
        public string SpaceNumber { get; set; }
        public string Designation { get; set; }
        public double AreaSqMeters { get; set; }
        public ElementId BoundingElementId { get; set; }
        public string BoundingCategoryName { get; set; }
        public string ElementName { get; set; }
    }

    public class HeatLossCalculationResult
    {
        public int SpacesProcessedCount { get; set; }
        public int CubesPlacedCount { get; set; }
        public int DeletedCubesCount { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
    }

    public class HeatLossEngine
    {
        private readonly Document _doc;

        public HeatLossEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<RevitLinkInstance> GetRevitLinkInstances()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(link => link.GetLinkDocument() != null)
                .ToList();
        }

        public List<FamilySymbol> GetAvailableCubeSymbols()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .ToList();
        }

        public List<Space> GetTargetSpaces(string scopeMode, List<ElementId> selectedSpaceIds, ElementId levelId)
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpatialElement))
                .OfType<Space>()
                .Where(s => s.Area > 0);

            if (scopeMode == "Selected" && selectedSpaceIds != null && selectedSpaceIds.Count > 0)
            {
                var idSet = new HashSet<ElementId>(selectedSpaceIds);
                return collector.Where(s => idSet.Contains(s.Id)).ToList();
            }

            if (scopeMode == "Level" && levelId != null && levelId != ElementId.InvalidElementId)
            {
                return collector.Where(s => s.LevelId == levelId).ToList();
            }

            return collector.ToList();
        }

        public HeatLossCalculationResult ProcessSpaces(
            List<Space> spaces,
            FamilySymbol cubeSymbol,
            RevitLinkInstance selectedLinkInstance,
            string linkedParamName,
            string targetDesignationParamName,
            string targetAreaParamName,
            bool deleteExistingCubes,
            Action<string, double> progressCallback = null)
        {
            var result = new HeatLossCalculationResult();

            if (spaces == null || spaces.Count == 0)
            {
                result.Logs.Add("Нет пространств для обработки.");
                return result;
            }

            if (cubeSymbol == null)
            {
                result.Logs.Add("Не выбран типоразмер кубика.");
                return result;
            }

            if (!cubeSymbol.IsActive)
            {
                cubeSymbol.Activate();
                _doc.Regenerate();
            }

            if (deleteExistingCubes)
            {
                var existingCubes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol.Id == cubeSymbol.Id)
                    .Select(fi => fi.Id)
                    .ToList();

                if (existingCubes.Count > 0)
                {
                    _doc.Delete(existingCubes);
                    result.DeletedCubesCount = existingCubes.Count;
                    result.Logs.Add($"Удалено ранее расставленных кубиков: {existingCubes.Count}.");
                }
            }

            SpatialElementGeometryCalculator calculator = new SpatialElementGeometryCalculator(_doc);
            int totalSpaces = spaces.Count;

            for (int i = 0; i < totalSpaces; i++)
            {
                var space = spaces[i];
                double progressPct = ((double)(i + 1) / totalSpaces) * 100.0;
                progressCallback?.Invoke($"Анализ помещения/пространства {i + 1} из {totalSpaces}: {space.Name} ({space.Number})...", progressPct);

                List<HeatLossBoundaryItem> items = ExtractBoundaryItemsForSpace(space, calculator, selectedLinkInstance, linkedParamName);

                if (items.Count == 0)
                {
                    continue;
                }

                result.SpacesProcessedCount++;

                XYZ spaceCenter = GetSpaceCentroid(space);
                Level spaceLevel = _doc.GetElement(space.LevelId) as Level;

                int cubeIndex = 0;
                int cols = (int)Math.Ceiling(Math.Sqrt(items.Count));

                foreach (var item in items)
                {
                    int row = cubeIndex / cols;
                    int col = cubeIndex % cols;

                    double offsetX = (col - (cols - 1) / 2.0) * 1.5;
                    double offsetY = (row - (items.Count / cols) / 2.0) * 1.5;

                    XYZ cubePos = spaceCenter + new XYZ(offsetX, offsetY, 0);

                    FamilyInstance instance = _doc.Create.NewFamilyInstance(
                        cubePos,
                        cubeSymbol,
                        StructuralType.NonStructural);

                    if (instance != null)
                    {
                        result.CubesPlacedCount++;

                        if (spaceLevel != null)
                        {
                            Parameter pLevel = instance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                           ?? instance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                            if (pLevel != null && !pLevel.IsReadOnly)
                            {
                                pLevel.Set(spaceLevel.Id);
                            }
                        }

                        // Write designation parameter
                        WriteDesignationToCube(instance, item.Designation, targetDesignationParamName);

                        // Write area parameter
                        WriteAreaToCube(instance, item.AreaSqMeters, targetAreaParamName);

                        // Write space info parameters if present on cube
                        WriteSpaceInfoToCube(instance, space);
                    }

                    cubeIndex++;
                }
            }

            result.Logs.Add($"Успешно обработано пространств: {result.SpacesProcessedCount}, расставлено кубиков: {result.CubesPlacedCount}.");
            return result;
        }

        private List<HeatLossBoundaryItem> ExtractBoundaryItemsForSpace(
            Space space,
            SpatialElementGeometryCalculator calculator,
            RevitLinkInstance targetLinkInstance,
            string linkedParamName)
        {
            var boundaryItems = new List<HeatLossBoundaryItem>();
            var designationMap = new Dictionary<string, HeatLossBoundaryItem>();

            try
            {
                SpatialElementGeometryResults geomResults = calculator.CalculateSpatialElementGeometry(space);
                Solid spaceSolid = geomResults.GetGeometry();

                if (spaceSolid == null || spaceSolid.Faces.Size == 0) return boundaryItems;

                foreach (Face face in spaceSolid.Faces)
                {
                    IList<SpatialElementBoundarySubface> subfaces = geomResults.GetBoundaryFaceInfo(face);
                    if (subfaces == null) continue;

                    foreach (SpatialElementBoundarySubface subface in subfaces)
                    {
                        Face subfaceGeom = subface.GetSubface();
                        if (subfaceGeom == null) continue;

                        double areaSqFt = subfaceGeom.Area;
                        double areaSqM = UnitUtils.ConvertFromInternalUnits(areaSqFt, UnitTypeId.SquareMeters);
                        if (areaSqM < 0.001) continue;

                        LinkElementId boundingElementId = subface.SpatialBoundaryElement;
                        if (boundingElementId == null) continue;

                        Element boundingElement = null;
                        Document linkDoc = null;

                        if (boundingElementId.LinkInstanceId != ElementId.InvalidElementId)
                        {
                            if (targetLinkInstance != null && boundingElementId.LinkInstanceId != targetLinkInstance.Id)
                            {
                                continue;
                            }

                            RevitLinkInstance linkInst = _doc.GetElement(boundingElementId.LinkInstanceId) as RevitLinkInstance;
                            linkDoc = linkInst?.GetLinkDocument();
                            if (linkDoc != null)
                            {
                                boundingElement = linkDoc.GetElement(boundingElementId.LinkedElementId);
                            }
                        }
                        else if (boundingElementId.HostElementId != ElementId.InvalidElementId)
                        {
                            boundingElement = _doc.GetElement(boundingElementId.HostElementId);
                            linkDoc = _doc;
                        }

                        if (boundingElement == null) continue;

                        // Check designation of the host wall / boundary element
                        string wallDesignation = GetElementDesignation(boundingElement, linkedParamName);

                        // If element is a Wall, check for hosted windows & doors inserts inside wall!
                        double insertsTotalAreaSqM = 0.0;

                        if (boundingElement is Wall wall && linkDoc != null)
                        {
                            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
                            foreach (ElementId insertId in insertIds)
                            {
                                Element insertElem = linkDoc.GetElement(insertId);
                                if (insertElem == null) continue;

                                string insertDesignation = GetElementDesignation(insertElem, linkedParamName);
                                if (string.IsNullOrWhiteSpace(insertDesignation)) continue;

                                double insertAreaSqM = GetInsertAreaSqMeters(insertElem);
                                if (insertAreaSqM > 0.001)
                                {
                                    insertsTotalAreaSqM += insertAreaSqM;
                                    AddOrAggregateItem(designationMap, space, insertDesignation, insertAreaSqM, insertElem);
                                }
                            }
                        }

                        // Add main wall/boundary element if designation exists
                        if (!string.IsNullOrWhiteSpace(wallDesignation))
                        {
                            double netAreaSqM = Math.Max(0.0, areaSqM - insertsTotalAreaSqM);
                            if (netAreaSqM > 0.001)
                            {
                                AddOrAggregateItem(designationMap, space, wallDesignation, netAreaSqM, boundingElement);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore individual space geometry errors
            }

            return designationMap.Values.ToList();
        }

        private void AddOrAggregateItem(
            Dictionary<string, HeatLossBoundaryItem> map,
            Space space,
            string designation,
            double areaSqM,
            Element element)
        {
            designation = designation.Trim();
            if (map.ContainsKey(designation))
            {
                map[designation].AreaSqMeters += areaSqM;
            }
            else
            {
                map[designation] = new HeatLossBoundaryItem
                {
                    SpaceId = space.Id,
                    SpaceName = space.Name,
                    SpaceNumber = space.Number,
                    Designation = designation,
                    AreaSqMeters = areaSqM,
                    BoundingElementId = element.Id,
                    BoundingCategoryName = element.Category?.Name ?? "Элемент",
                    ElementName = element.Name
                };
            }
        }

        private double GetInsertAreaSqMeters(Element insertElem)
        {
            double widthFt = GetParamDoubleValue(insertElem, "Ширина", "Width", "ADSK_Размер_Ширина");
            double heightFt = GetParamDoubleValue(insertElem, "Высота", "Height", "ADSK_Размер_Высота");

            if (widthFt > 0 && heightFt > 0)
            {
                double areaSqFt = widthFt * heightFt;
                return UnitUtils.ConvertFromInternalUnits(areaSqFt, UnitTypeId.SquareMeters);
            }

            BoundingBoxXYZ bbox = insertElem.get_BoundingBox(null);
            if (bbox != null)
            {
                double dx = Math.Abs(bbox.Max.X - bbox.Min.X);
                double dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                double dz = Math.Abs(bbox.Max.Z - bbox.Min.Z);
                double span = Math.Max(dx, dy);
                double areaSqFt = span * dz;
                return UnitUtils.ConvertFromInternalUnits(areaSqFt, UnitTypeId.SquareMeters);
            }

            return 0.0;
        }

        private double GetParamDoubleValue(Element element, params string[] paramNames)
        {
            if (element == null) return 0.0;

            foreach (string name in paramNames)
            {
                Parameter p = element.LookupParameter(name);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    return p.AsDouble();
                }

                // Check type parameter
                ElementId typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = element.Document.GetElement(typeId);
                    if (typeElem != null)
                    {
                        Parameter tp = typeElem.LookupParameter(name);
                        if (tp != null && tp.HasValue && tp.StorageType == StorageType.Double)
                        {
                            return tp.AsDouble();
                        }
                    }
                }
            }

            return 0.0;
        }

        private string GetElementDesignation(Element element, string paramName)
        {
            if (element == null) return null;

            string[] candidates = new string[]
            {
                paramName,
                "ADSK_Обозначение",
                "ADSK_Марка",
                "Марка",
                "Обозначение"
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                // Instance param
                Parameter param = element.LookupParameter(candidate);
                if (param != null && param.HasValue)
                {
                    string val = param.AsString();
                    if (string.IsNullOrWhiteSpace(val)) val = param.AsValueString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }

                // Type param
                ElementId typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = element.Document.GetElement(typeId);
                    if (typeElem != null)
                    {
                        Parameter typeParam = typeElem.LookupParameter(candidate);
                        if (typeParam != null && typeParam.HasValue)
                        {
                            string val = typeParam.AsString();
                            if (string.IsNullOrWhiteSpace(val)) val = typeParam.AsValueString();
                            if (!string.IsNullOrWhiteSpace(val)) return val;
                        }
                    }
                }
            }

            return null;
        }

        private void WriteDesignationToCube(FamilyInstance cube, string designation, string userParamName)
        {
            string[] candidates = new string[]
            {
                userParamName,
                "ADSK_Обозначение",
                "ADSK_Марка",
                "Марка",
                "Обозначение",
                "Комментарии"
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                Parameter p = cube.LookupParameter(candidate);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(designation);
                    return;
                }
            }

            // Fallback to BuiltInParameter.ALL_MODEL_MARK
            Parameter pMark = cube.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (pMark != null && !pMark.IsReadOnly && pMark.StorageType == StorageType.String)
            {
                pMark.Set(designation);
            }
        }

        private void WriteAreaToCube(FamilyInstance cube, double areaSqM, string userParamName)
        {
            string[] candidates = new string[]
            {
                userParamName,
                "ADSK_Площадь",
                "Площадь",
                "ADSK_Значение",
                "Значение"
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                Parameter p = cube.LookupParameter(candidate);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Double)
                    {
                        double internalArea = UnitUtils.ConvertToInternalUnits(areaSqM, UnitTypeId.SquareMeters);
                        p.Set(internalArea);
                        return;
                    }
                    else if (p.StorageType == StorageType.String)
                    {
                        p.Set(areaSqM.ToString("F2"));
                        return;
                    }
                }
            }
        }

        private void WriteSpaceInfoToCube(FamilyInstance cube, Space space)
        {
            Parameter pSpaceName = cube.LookupParameter("Имя пространства") ?? cube.LookupParameter("ADSK_Имя помещения");
            if (pSpaceName != null && !pSpaceName.IsReadOnly && pSpaceName.StorageType == StorageType.String)
            {
                pSpaceName.Set(space.Name);
            }

            Parameter pSpaceNumber = cube.LookupParameter("Номер пространства") ?? cube.LookupParameter("ADSK_Номер помещения");
            if (pSpaceNumber != null && !pSpaceNumber.IsReadOnly && pSpaceNumber.StorageType == StorageType.String)
            {
                pSpaceNumber.Set(space.Number);
            }
        }

        private XYZ GetSpaceCentroid(Space space)
        {
            LocationPoint locPoint = space.Location as LocationPoint;
            if (locPoint != null)
            {
                return locPoint.Point;
            }

            BoundingBoxXYZ bbox = space.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) * 0.5;
            }

            return XYZ.Zero;
        }
    }
}
