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

        /// <summary>
        /// Collect all Revit link instances in the document.
        /// </summary>
        public List<RevitLinkInstance> GetRevitLinkInstances()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(link => link.GetLinkDocument() != null)
                .ToList();
        }

        /// <summary>
        /// Collect all Generic Model family symbols suitable for cube placement.
        /// </summary>
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

        /// <summary>
        /// Collect target spaces in document based on filter selection.
        /// </summary>
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

        /// <summary>
        /// Main execution method: calculates boundary subfaces and places cube family instances in spaces.
        /// </summary>
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

            // Ensure cube symbol is active
            if (!cubeSymbol.IsActive)
            {
                cubeSymbol.Activate();
                _doc.Regenerate();
            }

            // Optional cleanup of previously placed cubes
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
                progressCallback?.Invoke($"Анализ пространства {i + 1} из {totalSpaces}: {space.Name} ({space.Number})...", progressPct);

                List<HeatLossBoundaryItem> items = ExtractBoundaryItemsForSpace(space, calculator, selectedLinkInstance, linkedParamName);

                if (items.Count == 0)
                {
                    continue;
                }

                result.SpacesProcessedCount++;

                // Place cubes inside space
                XYZ spaceCenter = GetSpaceCentroid(space);
                Level spaceLevel = _doc.GetElement(space.LevelId) as Level;

                int cubeIndex = 0;
                int cols = (int)Math.Ceiling(Math.Sqrt(items.Count));

                foreach (var item in items)
                {
                    int row = cubeIndex / cols;
                    int col = cubeIndex % cols;

                    // Offset position by 1.5 ft (~450 mm) grid to avoid overlap
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

                        // Associate level parameter if available
                        if (spaceLevel != null)
                        {
                            Parameter pLevel = instance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                           ?? instance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                            if (pLevel != null && !pLevel.IsReadOnly)
                            {
                                pLevel.Set(spaceLevel.Id);
                            }
                        }

                        // Write designation parameter (e.g. ADSK_Обозначение)
                        if (!string.IsNullOrEmpty(targetDesignationParamName))
                        {
                            Parameter pDesig = instance.LookupParameter(targetDesignationParamName);
                            if (pDesig != null && !pDesig.IsReadOnly)
                            {
                                if (pDesig.StorageType == StorageType.String)
                                {
                                    pDesig.Set(item.Designation);
                                }
                            }
                        }

                        // Write area parameter (e.g. ADSK_Площадь / Площадь)
                        if (!string.IsNullOrEmpty(targetAreaParamName))
                        {
                            Parameter pArea = instance.LookupParameter(targetAreaParamName);
                            if (pArea != null && !pArea.IsReadOnly)
                            {
                                if (pArea.StorageType == StorageType.Double)
                                {
                                    // Internal units are sq feet. Convert sq meters to internal units: sqM / 0.09290304
                                    double internalArea = UnitUtils.ConvertToInternalUnits(item.AreaSqMeters, UnitTypeId.SquareMeters);
                                    pArea.Set(internalArea);
                                }
                                else if (pArea.StorageType == StorageType.String)
                                {
                                    pArea.Set(item.AreaSqMeters.ToString("F2"));
                                }
                            }
                        }
                    }

                    cubeIndex++;
                }
            }

            result.Logs.Add($"Успешно обработано пространств: {result.SpacesProcessedCount}, расставлено кубиков: {result.CubesPlacedCount}.");
            return result;
        }

        /// <summary>
        /// Extracts bounding element items and calculates surface areas for a single space.
        /// </summary>
        private List<HeatLossBoundaryItem> ExtractBoundaryItemsForSpace(
            Space space,
            SpatialElementGeometryCalculator calculator,
            RevitLinkInstance targetLinkInstance,
            string linkedParamName)
        {
            var boundaryMap = new Dictionary<string, HeatLossBoundaryItem>();

            try
            {
                SpatialElementGeometryResults geomResults = calculator.CalculateSpatialElementGeometry(space);
                Solid spaceSolid = geomResults.GetGeometry();

                if (spaceSolid == null || spaceSolid.Faces.Size == 0) return new List<HeatLossBoundaryItem>();

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

                        // Check if boundary element is from linked model
                        if (boundingElementId.LinkInstanceId != ElementId.InvalidElementId)
                        {
                            if (targetLinkInstance != null && boundingElementId.LinkInstanceId != targetLinkInstance.Id)
                            {
                                continue; // Skip links that user did not select
                            }

                            RevitLinkInstance linkInst = _doc.GetElement(boundingElementId.LinkInstanceId) as RevitLinkInstance;
                            Document linkDoc = linkInst?.GetLinkDocument();
                            if (linkDoc != null)
                            {
                                boundingElement = linkDoc.GetElement(boundingElementId.LinkedElementId);
                            }
                        }
                        else if (boundingElementId.HostElementId != ElementId.InvalidElementId)
                        {
                            boundingElement = _doc.GetElement(boundingElementId.HostElementId);
                        }

                        if (boundingElement == null) continue;

                        // Read ADSK_Обозначение (or specified parameter)
                        string designation = GetElementDesignation(boundingElement, linkedParamName);
                        if (string.IsNullOrWhiteSpace(designation)) continue;

                        designation = designation.Trim();

                        // Aggregate area per designation within this space
                        if (boundaryMap.ContainsKey(designation))
                        {
                            boundaryMap[designation].AreaSqMeters += areaSqM;
                        }
                        else
                        {
                            boundaryMap[designation] = new HeatLossBoundaryItem
                            {
                                SpaceId = space.Id,
                                SpaceName = space.Name,
                                SpaceNumber = space.Number,
                                Designation = designation,
                                AreaSqMeters = areaSqM,
                                BoundingElementId = boundingElement.Id,
                                BoundingCategoryName = boundingElement.Category?.Name ?? "Элемент"
                            };
                        }
                    }
                }
            }
            catch
            {
                // Ignore single space geometry calculation exceptions
            }

            return boundaryMap.Values.ToList();
        }

        /// <summary>
        /// Reads designation parameter value from element (checking instance parameter first, then type parameter).
        /// </summary>
        private string GetElementDesignation(Element element, string paramName)
        {
            if (element == null) return null;

            // 1. Instance parameter
            Parameter param = element.LookupParameter(paramName);
            if (param != null && param.HasValue)
            {
                string val = param.AsString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            // 2. Type parameter
            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element typeElem = element.Document.GetElement(typeId);
                if (typeElem != null)
                {
                    Parameter typeParam = typeElem.LookupParameter(paramName);
                    if (typeParam != null && typeParam.HasValue)
                    {
                        string val = typeParam.AsString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Computes 3D centroid of a space.
        /// </summary>
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
