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
        // 1. Номер помещения
        public string SpaceNumber { get; set; }

        // 2. Температура наружного воздуха (°C)
        public double OutdoorTemp { get; set; } = -23.0;

        // 3. Температура помещения (°C)
        public double IndoorTemp { get; set; } = 20.0;

        // 4. Наименование помещения
        public string SpaceName { get; set; }

        // 5. Обозначение ограждающей конструкции
        public string Designation { get; set; }

        // 6. Ориентация
        public string Orientation { get; set; } = "СЗ";

        // 7. Длина ограждающей конструкции (м)
        public double LengthMeters { get; set; }

        // 8. Высота ограждающей конструкции (м)
        public double HeightMeters { get; set; }

        // 9. Площадь (м²)
        public double AreaSqMeters { get; set; }

        // 10. Коэффициент n
        public double CoeffN { get; set; } = 1.0;

        // 11. Коэффициент теплопередачи k (Вт/(м²·°C))
        public double CoeffK { get; set; } = 1.0;

        // 12. b1 - поправка на ориентацию
        public double B1 { get; set; } = 0.1;

        // 13. b2 - поправка на угол
        public double B2 { get; set; } = 0.0;

        // 14. Коэффициент надбавки (1 + b1 + b2)
        public double CoeffAllowance => 1.0 + B1 + B2;

        // 15. Теплопотери (Вт) = (t_int - t_ext) * A * n * k * (1 + b1 + b2)
        public double HeatLossWatts => (IndoorTemp - OutdoorTemp) * AreaSqMeters * CoeffN * CoeffK * CoeffAllowance;

        // Additional internal references
        public ElementId SpaceId { get; set; }
        public ElementId BoundingElementId { get; set; }
        public string BoundingCategoryName { get; set; }
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
            double outdoorTemp,
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

                List<HeatLossBoundaryItem> items = ExtractBoundaryItemsForSpace(space, calculator, selectedLinkInstance, linkedParamName, outdoorTemp);

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

                        // Write all 15 parameters to the cube instance
                        WriteAllParametersToCube(instance, item, targetDesignationParamName, targetAreaParamName);
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
            string linkedParamName,
            double outdoorTemp)
        {
            var boundaryItems = new List<HeatLossBoundaryItem>();
            var designationMap = new Dictionary<string, HeatLossBoundaryItem>();

            double spaceHeightFt = space.UnboundedHeight > 0 ? space.UnboundedHeight : 9.84252; // default 3 meters
            double spaceHeightMeters = UnitUtils.ConvertFromInternalUnits(spaceHeightFt, UnitTypeId.Meters);

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

                        // Check window and door inserts in wall
                        if (boundingElement is Wall wall && linkDoc != null)
                        {
                            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
                            foreach (ElementId insertId in insertIds)
                            {
                                Element insertElem = linkDoc.GetElement(insertId);
                                if (insertElem == null) continue;

                                string insertDesignation = GetElementDesignation(insertElem, linkedParamName);
                                if (string.IsNullOrWhiteSpace(insertDesignation)) continue;

                                (double insertWidthM, double insertHeightM, double insertAreaSqM) = GetInsertDimensions(insertElem);
                                if (insertAreaSqM > 0.001)
                                {
                                    insertsTotalAreaSqM += insertAreaSqM;
                                    double kVal = GetThermalTransmittanceK(insertElem);
                                    AddOrAggregateItem(designationMap, space, insertDesignation, insertWidthM, insertHeightM, insertAreaSqM, kVal, outdoorTemp, insertElem);
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(wallDesignation))
                        {
                            double netAreaSqM = Math.Max(0.0, areaSqM - insertsTotalAreaSqM);
                            if (netAreaSqM > 0.001)
                            {
                                double heightM = spaceHeightMeters;
                                double lengthM = heightM > 0 ? netAreaSqM / heightM : Math.Sqrt(netAreaSqM);
                                double kVal = GetThermalTransmittanceK(boundingElement);

                                AddOrAggregateItem(designationMap, space, wallDesignation, lengthM, heightM, netAreaSqM, kVal, outdoorTemp, boundingElement);
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
            double lengthM,
            double heightM,
            double areaSqM,
            double kVal,
            double outdoorTemp,
            Element element)
        {
            designation = designation.Trim();
            if (map.ContainsKey(designation))
            {
                map[designation].AreaSqMeters += areaSqM;
                if (map[designation].HeightMeters > 0)
                {
                    map[designation].LengthMeters = map[designation].AreaSqMeters / map[designation].HeightMeters;
                }
            }
            else
            {
                map[designation] = new HeatLossBoundaryItem
                {
                    SpaceId = space.Id,
                    SpaceNumber = space.Number ?? "",
                    SpaceName = space.Name ?? "",
                    OutdoorTemp = outdoorTemp,
                    IndoorTemp = 20.0,
                    Designation = designation,
                    Orientation = "СЗ",
                    LengthMeters = lengthM,
                    HeightMeters = heightM,
                    AreaSqMeters = areaSqM,
                    CoeffN = 1.0,
                    CoeffK = kVal,
                    B1 = 0.1,
                    B2 = 0.0,
                    BoundingElementId = element.Id,
                    BoundingCategoryName = element.Category?.Name ?? "Элемент"
                };
            }
        }

        private (double widthM, double heightM, double areaSqM) GetInsertDimensions(Element insertElem)
        {
            double widthFt = GetParamDoubleValue(insertElem, "Ширина", "Width", "ADSK_Размер_Ширина");
            double heightFt = GetParamDoubleValue(insertElem, "Высота", "Height", "ADSK_Размер_Высота");

            if (widthFt > 0 && heightFt > 0)
            {
                double widthM = UnitUtils.ConvertFromInternalUnits(widthFt, UnitTypeId.Meters);
                double heightM = UnitUtils.ConvertFromInternalUnits(heightFt, UnitTypeId.Meters);
                double areaSqM = widthM * heightM;
                return (widthM, heightM, areaSqM);
            }

            BoundingBoxXYZ bbox = insertElem.get_BoundingBox(null);
            if (bbox != null)
            {
                double dx = Math.Abs(bbox.Max.X - bbox.Min.X);
                double dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                double dz = Math.Abs(bbox.Max.Z - bbox.Min.Z);
                double spanFt = Math.Max(dx, dy);
                double widthM = UnitUtils.ConvertFromInternalUnits(spanFt, UnitTypeId.Meters);
                double heightM = UnitUtils.ConvertFromInternalUnits(dz, UnitTypeId.Meters);
                double areaSqM = widthM * heightM;
                return (widthM, heightM, areaSqM);
            }

            return (0.0, 0.0, 0.0);
        }

        private double GetThermalTransmittanceK(Element element)
        {
            // 1. Look for thermal resistance R (ADSK_Сопротивление_теплопередаче / R)
            double rVal = GetParamDoubleValue(element,
                "ADSK_Сопротивление_теплопередаче",
                "Сопротивление_теплопередаче",
                "R_сопротивление",
                "R");

            if (rVal > 0.0001)
            {
                return 1.0 / rVal;
            }

            // 2. Look for thermal transmittance k / U (ADSK_Коэффициент_теплопередачи / k / U-Value)
            double kVal = GetParamDoubleValue(element,
                "ADSK_Коэффициент_теплопередачи",
                "Коэффициент_теплопередачи",
                "k",
                "U-Value",
                "U_Value");

            if (kVal > 0.0001)
            {
                return kVal;
            }

            // Default fallback
            return 1.0;
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

        private void WriteAllParametersToCube(FamilyInstance cube, HeatLossBoundaryItem item, string userDesignationParam, string userAreaParam)
        {
            if (cube == null || item == null) return;

            // 1. Номер помещения
            SetCubeParamValue(cube, item.SpaceNumber, "ADSK_Номер помещения", "ADSK_Номер пространства", "Номер помещения", "Номер пространства", "Номер");

            // 2. Температура наружного воздуха
            SetCubeParamValue(cube, item.OutdoorTemp, "ADSK_Температура наружного воздуха", "Температура наружного воздуха", "t_ext");

            // 3. Температура помещения
            SetCubeParamValue(cube, item.IndoorTemp, "ADSK_Температура помещения", "Температура помещения", "t_int");

            // 4. Наименование помещения
            SetCubeParamValue(cube, item.SpaceName, "ADSK_Имя помещения", "ADSK_Имя пространства", "Имя помещения", "Имя пространства", "Наименование", "Имя");

            // 5. Обозначение ограждающей конструкции
            SetCubeParamValue(cube, item.Designation, userDesignationParam, "ADSK_Обозначение", "ADSK_Марка", "Марка", "Обозначение");

            // 6. Ориентация
            SetCubeParamValue(cube, item.Orientation, "ADSK_Ориентация", "Ориентация");

            // 7. Длина конструкции
            SetCubeParamValue(cube, item.LengthMeters, "ADSK_Длина", "Длина");

            // 8. Высота конструкции
            SetCubeParamValue(cube, item.HeightMeters, "ADSK_Высота", "Высота");

            // 9. Площадь
            SetCubeParamValue(cube, item.AreaSqMeters, userAreaParam, "ADSK_Площадь", "Площадь", "ADSK_Значение");

            // 10. Коэффициент n
            SetCubeParamValue(cube, item.CoeffN, "ADSK_Коэффициент_n", "Коэффициент_n", "n");

            // 11. Коэффициент теплопередачи k
            SetCubeParamValue(cube, item.CoeffK, "ADSK_Коэффициент_теплопередачи", "Коэффициент_теплопередачи", "k");

            // 12. b1
            SetCubeParamValue(cube, item.B1, "ADSK_b1", "b1");

            // 13. b2
            SetCubeParamValue(cube, item.B2, "ADSK_b2", "b2");

            // 14. Коэффициент надбавки
            SetCubeParamValue(cube, item.CoeffAllowance, "ADSK_Коэффициент_надбавки", "Коэффициент_надбавки", "Надбавка");

            // 15. Теплопотери (Вт)
            SetCubeParamValue(cube, item.HeatLossWatts, "ADSK_Теплопотери", "Теплопотери", "Q");
        }

        private void SetCubeParamValue(FamilyInstance cube, string strValue, params string[] paramNames)
        {
            foreach (string name in paramNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                Parameter p = cube.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(strValue ?? "");
                    return;
                }
            }

            // Fallback mark check
            if (paramNames.Contains("ADSK_Обозначение") || paramNames.Contains("Марка"))
            {
                Parameter pMark = cube.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (pMark != null && !pMark.IsReadOnly && pMark.StorageType == StorageType.String)
                {
                    pMark.Set(strValue ?? "");
                }
            }
        }

        private void SetCubeParamValue(FamilyInstance cube, double doubleValue, params string[] paramNames)
        {
            foreach (string name in paramNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                Parameter p = cube.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Double)
                    {
                        p.Set(doubleValue);
                        return;
                    }
                    else if (p.StorageType == StorageType.String)
                    {
                        p.Set(doubleValue.ToString("F2"));
                        return;
                    }
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

                foreach (var sf in schedulableFields)
                {
                    string fName = sf.GetName(_doc);

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

                var countSf = schedulableFields.FirstOrDefault(sf =>
                    sf.GetName(_doc).IndexOf("Число", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sf.GetName(_doc).IndexOf("Количество", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sf.GetName(_doc).IndexOf("Count", StringComparison.OrdinalIgnoreCase) >= 0);
                if (countSf != null)
                {
                    definition.AddField(countSf);
                }

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
                // Exact 15 columns requested by user
                sb.AppendLine("1. Номер помещения;2. Температура наружного воздуха (°C);3. Температура помещения (°C);4. Наименование помещения;5. Обозначение конструкций;6. Ориентация;7. Длина (м);8. Высота (м);9. Площадь (м²);10. Коэффициент n;11. Коэффициент k;12. b1;13. b2;14. Коэффициент надбавки;15. Теплопотери (Вт)");

                foreach (var item in items)
                {
                    string line = $"{EscapeCsv(item.SpaceNumber)};" +
                                  $"{item.OutdoorTemp:F1};" +
                                  $"{item.IndoorTemp:F1};" +
                                  $"{EscapeCsv(item.SpaceName)};" +
                                  $"{EscapeCsv(item.Designation)};" +
                                  $"{EscapeCsv(item.Orientation)};" +
                                  $"{item.LengthMeters:F2};" +
                                  $"{item.HeightMeters:F2};" +
                                  $"{item.AreaSqMeters:F2};" +
                                  $"{item.CoeffN:F2};" +
                                  $"{item.CoeffK:F3};" +
                                  $"{item.B1:F2};" +
                                  $"{item.B2:F2};" +
                                  $"{item.CoeffAllowance:F2};" +
                                  $"{item.HeatLossWatts:F1}";

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
