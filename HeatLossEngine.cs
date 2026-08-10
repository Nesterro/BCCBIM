using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public ViewSchedule CreatedSchedule { get; set; }
        public string ExportedCsvPath { get; set; }
        public List<HeatLossBoundaryItem> ExtractedItems { get; set; } = new List<HeatLossBoundaryItem>();
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
            bool createSchedule,
            bool exportCsv,
            string csvExportPath,
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
                double progressPct = ((double)(i + 1) / totalSpaces) * 70.0;
                progressCallback?.Invoke($"Анализ помещения/пространства {i + 1} из {totalSpaces}: {space.Name} ({space.Number})...", progressPct);

                List<HeatLossBoundaryItem> items = ExtractBoundaryItemsForSpace(space, calculator, selectedLinkInstance, linkedParamName);

                if (items.Count == 0)
                {
                    continue;
                }

                result.SpacesProcessedCount++;
                result.ExtractedItems.AddRange(items);

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

                        WriteDesignationToCube(instance, item.Designation, targetDesignationParamName);
                        WriteAreaToCube(instance, item.AreaSqMeters, targetAreaParamName);
                        WriteSpaceInfoToCube(instance, space);
                    }

                    cubeIndex++;
                }
            }

            result.Logs.Add($"Успешно обработано пространств: {result.SpacesProcessedCount}, расставлено кубиков: {result.CubesPlacedCount}.");

            // Option 1: Create Revit ViewSchedule
            if (createSchedule)
            {
                progressCallback?.Invoke("Формирование спецификации в Revit...", 80.0);
                result.CreatedSchedule = CreateOrUpdateRevitSchedule(targetDesignationParamName, targetAreaParamName);
                if (result.CreatedSchedule != null)
                {
                    result.Logs.Add($"Создана спецификация в Revit: '{result.CreatedSchedule.Name}'.");
                }
            }

            // Option 2: Export to CSV / Excel report
            if (exportCsv && !string.IsNullOrWhiteSpace(csvExportPath))
            {
                progressCallback?.Invoke("Экспорт отчёта в CSV/Excel...", 90.0);
                string exportedPath = ExportToCsvReport(result.ExtractedItems, csvExportPath);
                if (!string.IsNullOrEmpty(exportedPath))
                {
                    result.ExportedCsvPath = exportedPath;
                    result.Logs.Add($"Отчёт сохранен в файл: {exportedPath}");
                }
            }

            progressCallback?.Invoke("Готово!", 100.0);
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

                        string wallDesignation = GetElementDesignation(boundingElement, linkedParamName);
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

                Parameter param = element.LookupParameter(candidate);
                if (param != null && param.HasValue)
                {
                    string val = param.AsString();
                    if (string.IsNullOrWhiteSpace(val)) val = param.AsValueString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }

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
            if (cube == null || space == null) return;

            // Room / Space Number
            string[] numberCandidates = new string[]
            {
                "ADSK_Номер помещения",
                "ADSK_Номер пространства",
                "Номер помещения",
                "Номер пространства",
                "Номер_помещения",
                "Номер_пространства",
                "Номер"
            };

            foreach (string candidate in numberCandidates)
            {
                Parameter p = cube.LookupParameter(candidate);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(space.Number ?? "");
                    break;
                }
            }

            // Room / Space Name
            string[] nameCandidates = new string[]
            {
                "ADSK_Имя помещения",
                "ADSK_Имя пространства",
                "Имя помещения",
                "Имя пространства",
                "Имя_помещения",
                "Имя_пространства",
                "Имя",
                "Наименование"
            };

            foreach (string candidate in nameCandidates)
            {
                Parameter p = cube.LookupParameter(candidate);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(space.Name ?? "");
                    break;
                }
            }
        }

        private ViewSchedule CreateOrUpdateRevitSchedule(string targetDesignationParamName, string targetAreaParamName)
        {
            try
            {
                string scheduleName = "Спецификация ограждающих конструкций (Теплопотери)";

                ViewSchedule existingSchedule = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

                if (existingSchedule != null)
                {
                    return existingSchedule;
                }

                ViewSchedule newSchedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                newSchedule.Name = scheduleName;

                ScheduleDefinition definition = newSchedule.Definition;
                var schedulableFields = definition.GetSchedulableFields();

                ScheduleField fieldNumber = null;
                ScheduleField fieldName = null;
                ScheduleField fieldDesignation = null;
                ScheduleField fieldArea = null;

                // Add ONLY required clean parameters (Room Number, Room Name, Designation, Area, Count)
                foreach (var sf in schedulableFields)
                {
                    string fName = sf.GetName(_doc);

                    // Skip unnecessary parameters (Family, Type, Level, Category, Image, etc.)
                    if (fName.IndexOf("Семейство", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Тип", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Категория", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Уровень", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Изображение", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Комментарии", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (fName.IndexOf("Номер помещения", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("Номер пространства", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fName.IndexOf("ADSK_Номер", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (fieldNumber == null) fieldNumber = definition.AddField(sf);
                    }
                    else if (fName.IndexOf("Имя помещения", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("Имя пространства", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("ADSK_Имя", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (fieldName == null) fieldName = definition.AddField(sf);
                    }
                    else if (fName.IndexOf(targetDesignationParamName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("ADSK_Обозначение", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("Марка", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("Обозначение", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (fieldDesignation == null) fieldDesignation = definition.AddField(sf);
                    }
                    else if (fName.IndexOf(targetAreaParamName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("ADSK_Площадь", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fName.IndexOf("Площадь", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (fieldArea == null)
                        {
                            fieldArea = definition.AddField(sf);
                            fieldArea.DisplayType = ScheduleFieldDisplayType.Totals;
                        }
                    }
                }

                // Add Count field
                var countSf = schedulableFields.FirstOrDefault(sf =>
                    sf.GetName(_doc).IndexOf("Число", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sf.GetName(_doc).IndexOf("Количество", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sf.GetName(_doc).IndexOf("Count", StringComparison.OrdinalIgnoreCase) >= 0);
                if (countSf != null)
                {
                    definition.AddField(countSf);
                }

                // Grouping & Sorting by Room Number then Designation
                if (fieldNumber != null)
                {
                    ScheduleSortGroupField sortGroupNumber = new ScheduleSortGroupField(fieldNumber.FieldId);
                    sortGroupNumber.ShowHeader = true;
                    sortGroupNumber.ShowBlankLine = true;
                    definition.AddSortGroupField(sortGroupNumber);
                }

                if (fieldDesignation != null)
                {
                    ScheduleSortGroupField sortGroupDesig = new ScheduleSortGroupField(fieldDesignation.FieldId);
                    definition.AddSortGroupField(sortGroupDesig);
                }

                return newSchedule;
            }
            catch
            {
                return null;
            }
        }

        private string ExportToCsvReport(List<HeatLossBoundaryItem> items, string csvFilePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(csvFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Номер помещения;Наименование помещения;Обозначение конструкции;Категория;Площадь (м2)");

                foreach (var item in items)
                {
                    string line = $"{EscapeCsv(item.SpaceNumber)};{EscapeCsv(item.SpaceName)};{EscapeCsv(item.Designation)};{EscapeCsv(item.BoundingCategoryName)};{item.AreaSqMeters:F2}";
                    sb.AppendLine(line);
                }

                File.WriteAllText(csvFilePath, sb.ToString(), Encoding.UTF8);
                return csvFilePath;
            }
            catch
            {
                return null;
            }
        }

        private string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(";") || val.Contains("\"") || val.Contains("\n"))
            {
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            }
            return val;
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
