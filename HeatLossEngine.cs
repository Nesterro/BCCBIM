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

    public class PlacedCubeInfo
    {
        public FamilyInstance Instance { get; set; }
        public HeatLossBoundaryItem ItemData { get; set; }
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

        // -----------------------------------------------------------------------
        // ITERATION 1 — STEP A (call OUTSIDE any transaction)
        // Write the shared param file, open it, collect ExternalDefinition objects.
        // Does NOT restore SharedParametersFilename — caller must call
        // RestoreSharedParamFilename() AFTER the transaction that calls
        // -----------------------------------------------------------------------
        public List<ExternalDefinition> PrepareSharedParamDefinitions(
            out string origFilename, HeatLossCalculationResult result)
        {
            origFilename = null;
            var definitions = new List<ExternalDefinition>();

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "BIMBCC");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string bimbccFilePath = Path.Combine(tempDir, "BIMBCC_HeatLoss_SharedParams.txt");

                // Write file as UTF-16 LE (Revit's required encoding for shared param files)
                // Use explicit StreamWriter so we control BOM and line endings precisely
                using (var sw = new StreamWriter(bimbccFilePath, false, Encoding.Unicode))
                {
                    sw.WriteLine("# This is a Revit shared parameter file.");
                    sw.WriteLine("# Do not edit manually.");
                    sw.WriteLine("*META\tVERSION\tMINVER");
                    sw.WriteLine("META\t2\t1");
                    sw.WriteLine("*GROUP\tID\tNAME");
                    sw.WriteLine("GROUP\t1\tBIMBCC_Теплопотери");
                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
                    sw.WriteLine("PARAM\te1b2c3d4-0001-4000-8000-000000000001\tBIMBCC_Номер помещения\tTEXT\t\t1\t1\tНомер помещения/пространства\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0002-4000-8000-000000000002\tBIMBCC_Температура наружного воздуха\tNUMBER\t\t1\t1\tТемпература наружного воздуха\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0003-4000-8000-000000000003\tBIMBCC_Температура помещения\tNUMBER\t\t1\t1\tТемпература помещения\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0004-4000-8000-000000000004\tBIMBCC_Имя помещения\tTEXT\t\t1\t1\tНаименование помещения\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0005-4000-8000-000000000005\tBIMBCC_Обозначение\tTEXT\t\t1\t1\tОбозначение конструкции\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0006-4000-8000-000000000006\tBIMBCC_Ориентация\tTEXT\t\t1\t1\tОриентация конструкции\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0007-4000-8000-000000000007\tBIMBCC_Длина\tLENGTH\t\t1\t1\tДлина конструкции\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0008-4000-8000-000000000008\tBIMBCC_Высота\tLENGTH\t\t1\t1\tВысота конструкции\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0009-4000-8000-000000000009\tBIMBCC_Площадь\tAREA\t\t1\t1\tПлощадь конструкции\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0010-4000-8000-000000000010\tBIMBCC_Коэффициент_n\tNUMBER\t\t1\t1\tКоэффициент n\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0011-4000-8000-000000000011\tBIMBCC_Коэффициент_теплопередачи\tNUMBER\t\t1\t1\tКоэффициент теплопередачи k\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0012-4000-8000-000000000012\tBIMBCC_b1\tNUMBER\t\t1\t1\tПоправка b1\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0013-4000-8000-000000000013\tBIMBCC_b2\tNUMBER\t\t1\t1\tПоправка b2\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0014-4000-8000-000000000014\tBIMBCC_Коэффициент_надбавки\tNUMBER\t\t1\t1\tКоэффициент надбавки\t1\t0");
                    sw.WriteLine("PARAM\te1b2c3d4-0015-4000-8000-000000000015\tBIMBCC_Теплопотери\tNUMBER\t\t1\t1\tТеплопотери Q (Вт)\t1\t0");
                }

                // Save original filename BEFORE switching
                try { origFilename = _doc.Application.SharedParametersFilename; } catch { }

                // Switch to BIMBCC file — NOTE: this is OUTSIDE any transaction
                _doc.Application.SharedParametersFilename = bimbccFilePath;
                DefinitionFile defFile = _doc.Application.OpenSharedParameterFile();

                if (defFile == null)
                {
                    result?.Logs.Add("Итерация 1 (подготовка): Не удалось открыть BIMBCC_HeatLoss_SharedParams.txt.");
                    return definitions;
                }

                DefinitionGroup group = defFile.Groups.get_Item("BIMBCC_Теплопотери")
                                     ?? defFile.Groups.Create("BIMBCC_Теплопотери");

                // Build ExternalDefinition list — FILE IS STILL OPEN, filename unchanged
                var paramsToEnsure = new (string name, ForgeTypeId forgeType)[]
                {
                    ("BIMBCC_Номер помещения",               SpecTypeId.String.Text),
                    ("BIMBCC_Температура наружного воздуха", SpecTypeId.Number),
                    ("BIMBCC_Температура помещения",         SpecTypeId.Number),
                    ("BIMBCC_Имя помещения",                 SpecTypeId.String.Text),
                    ("BIMBCC_Обозначение",                   SpecTypeId.String.Text),
                    ("BIMBCC_Ориентация",                    SpecTypeId.String.Text),
                    ("BIMBCC_Длина",                         SpecTypeId.Length),
                    ("BIMBCC_Высота",                        SpecTypeId.Length),
                    ("BIMBCC_Площадь",                       SpecTypeId.Area),
                    ("BIMBCC_Коэффициент_n",                 SpecTypeId.Number),
                    ("BIMBCC_Коэффициент_теплопередачи",     SpecTypeId.Number),
                    ("BIMBCC_b1",                            SpecTypeId.Number),
                    ("BIMBCC_b2",                            SpecTypeId.Number),
                    ("BIMBCC_Коэффициент_надбавки",          SpecTypeId.Number),
                    ("BIMBCC_Теплопотери",                   SpecTypeId.Number)
                };

                foreach (var item in paramsToEnsure)
                {
                    Definition def = group.Definitions.get_Item(item.name);
                    if (def == null)
                    {
                        try
                        {
                            def = group.Definitions.Create(
                                new ExternalDefinitionCreationOptions(item.name, item.forgeType)
                                { UserModifiable = true, Visible = true });
                        }
                        catch { }
                    }
                    if (def is ExternalDefinition extDef) definitions.Add(extDef);
                }

                result?.Logs.Add($"Итерация 1 (подготовка): definitions готово: {definitions.Count}/15.");
            }
            catch (Exception ex)
            {
                result?.Logs.Add($"Итерация 1 (подготовка): {ex.Message}");
                // Restore on error
                try
                {
                    if (!string.IsNullOrEmpty(origFilename) && File.Exists(origFilename))
                        _doc.Application.SharedParametersFilename = origFilename;
                }
                catch { }
                origFilename = null; // signal to caller that restore already done
            }

            return definitions;
        }

        // -----------------------------------------------------------------------
        // ITERATION 1 — STEP B (call INSIDE Transaction 1)
        // Bind each ExternalDefinition to OST_GenericModel.
        // -----------------------------------------------------------------------
        public void InsertParameterBindings(
            List<ExternalDefinition> definitions, HeatLossCalculationResult result)
        {
            if (definitions == null || definitions.Count == 0)
            {
                result?.Logs.Add("Итерация 1 (привязка): список definitions пуст, пропускаем.");
                return;
            }

            try
            {
                Category genModelCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                if (genModelCat == null)
                {
                    result?.Logs.Add("Итерация 1 (привязка): категория OST_GenericModel не найдена.");
                    return;
                }

                CategorySet catSet = _doc.Application.Create.NewCategorySet();
                catSet.Insert(genModelCat);
                InstanceBinding binding = _doc.Application.Create.NewInstanceBinding(catSet);

                int boundCount = 0, alreadyCount = 0;
                foreach (ExternalDefinition def in definitions)
                {
                    bool alreadyBound = false;
                    var it = _doc.ParameterBindings.ForwardIterator();
                    while (it.MoveNext())
                    {
                        if (it.Key is Definition existingKey && existingKey.Name == def.Name)
                        {
                            alreadyBound = true;
                            if (it.Current is ElementBinding existingBnd
                                && !existingBnd.Categories.Contains(genModelCat))
                            {
                                existingBnd.Categories.Insert(genModelCat);
                                _doc.ParameterBindings.ReInsert(def, existingBnd, BuiltInParameterGroup.PG_DATA);
                            }
                            alreadyCount++;
                            break;
                        }
                    }

                    if (!alreadyBound)
                    {
                        bool ok = _doc.ParameterBindings.Insert(def, binding, BuiltInParameterGroup.PG_DATA);
                        if (!ok) ok = _doc.ParameterBindings.ReInsert(def, binding, BuiltInParameterGroup.PG_DATA);
                        if (ok) boundCount++;
                        else result?.Logs.Add($"  Не удалось привязать: {def.Name}");
                    }
                }

                result?.Logs.Add(
                    $"Итерация 1 (привязка): добавлено {boundCount}, уже было {alreadyCount}.");
                _doc.Regenerate();
            }
            catch (Exception ex)
            {
                result?.Logs.Add($"Итерация 1 (привязка): {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // ITERATION 1 — STEP C (call AFTER Transaction 1 commits)
        // Restore the original SharedParametersFilename.
        // -----------------------------------------------------------------------
        public void RestoreSharedParamFilename(string origFilename)
        {
            if (string.IsNullOrEmpty(origFilename)) return;
            try
            {
                if (File.Exists(origFilename))
                    _doc.Application.SharedParametersFilename = origFilename;
            }
            catch { }
        }

        // ITERATION 2: PLACE CUBES IN SPACES (TRANSACTION 2)
        public List<PlacedCubeInfo> PlaceCubeMarkers(
            List<Space> spaces,
            FamilySymbol cubeSymbol,
            RevitLinkInstance selectedLinkInstance,
            string linkedParamName,
            double outdoorTemp,
            bool deleteExistingCubes,
            HeatLossCalculationResult result,
            Action<string, double> progressCallback)
        {
            List<PlacedCubeInfo> placedList = new List<PlacedCubeInfo>();

            if (spaces == null || spaces.Count == 0 || cubeSymbol == null)
            {
                return placedList;
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
                    result.Logs.Add($"Итерация 2: Удалено ранее расставленных кубиков: {existingCubes.Count}.");
                }
            }

            SpatialElementGeometryCalculator calculator = new SpatialElementGeometryCalculator(_doc);
            int totalSpaces = spaces.Count;

            for (int i = 0; i < totalSpaces; i++)
            {
                var space = spaces[i];
                double pct = ((double)(i + 1) / totalSpaces) * 100.0;
                progressCallback?.Invoke($"Итерация 2: Анализ помещения {i + 1}/{totalSpaces}: {space.Name}...", pct);

                List<HeatLossBoundaryItem> items = ExtractBoundaryItemsForSpace(space, calculator, selectedLinkInstance, linkedParamName, outdoorTemp);

                if (items.Count == 0) continue;

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

                        placedList.Add(new PlacedCubeInfo { Instance = instance, ItemData = item });
                    }

                    cubeIndex++;
                }
            }

            result.Logs.Add($"Итерация 2 (Расстановка): Расставлено кубиков по пространствам: {result.CubesPlacedCount}.");
            _doc.Regenerate();
            return placedList;
        }

        // ITERATION 3: OVERWRITE PARAMETERS ON CUBES & CREATE SCHEDULE (TRANSACTION 3)
        public void WriteParametersToCubesAndCreateSchedule(
            List<PlacedCubeInfo> placedCubes,
            string targetDesignationParamName,
            string targetAreaParamName,
            bool createSchedule,
            bool exportCsv,
            string csvExportPath,
            HeatLossCalculationResult result,
            Action<string, double> progressCallback)
        {
            if (placedCubes != null && placedCubes.Count > 0)
            {
                int totalCubes = placedCubes.Count;
                for (int i = 0; i < totalCubes; i++)
                {
                    var cubeInfo = placedCubes[i];
                    double pct = ((double)(i + 1) / totalCubes) * 50.0;
                    progressCallback?.Invoke($"Итерация 3: Перезапись параметров кубика {i + 1}/{totalCubes}...", pct);

                    WriteAllParametersToCube(cubeInfo.Instance, cubeInfo.ItemData, targetDesignationParamName, targetAreaParamName);
                }
                result.Logs.Add($"Итерация 3 (Запись): Перезаписаны параметры в {placedCubes.Count} кубиках.");
            }

            if (createSchedule)
            {
                progressCallback?.Invoke("Итерация 3: Формирование спецификации Revit...", 75.0);
                result.CreatedSchedule = CreateOrUpdateRevitSchedule(targetDesignationParamName, targetAreaParamName, result);
                if (result.CreatedSchedule != null)
                {
                    result.Logs.Add($"Сформирована спецификация в Revit: '{result.CreatedSchedule.Name}'.");
                }
            }

            if (exportCsv && !string.IsNullOrWhiteSpace(csvExportPath))
            {
                progressCallback?.Invoke("Итерация 3: Экспорт отчёта CSV/Excel...", 90.0);
                string exportedPath = ExportToCsvReport(result.ExtractedItems, csvExportPath);
                if (!string.IsNullOrEmpty(exportedPath))
                {
                    result.ExportedCsvPath = exportedPath;
                    result.Logs.Add($"Отчёт сохранен в файл: {exportedPath}");
                }
            }

            progressCallback?.Invoke("Готово!", 100.0);
        }

        private List<HeatLossBoundaryItem> ExtractBoundaryItemsForSpace(
            Space space,
            SpatialElementGeometryCalculator calculator,
            RevitLinkInstance targetLinkInstance,
            string linkedParamName,
            double outdoorTemp)
        {
            var boundaryItems = new List<HeatLossBoundaryItem>();

            double spaceHeightFt = space.UnboundedHeight > 0 ? space.UnboundedHeight : 9.84252;
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
                        if (string.IsNullOrWhiteSpace(wallDesignation))
                        {
                            wallDesignation = boundingElement.Name ?? boundingElement.Category?.Name ?? "Ограждающая конструкция";
                        }

                        double insertsTotalAreaSqM = 0.0;

                        if (boundingElement is Wall wall && linkDoc != null)
                        {
                            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
                            foreach (ElementId insertId in insertIds)
                            {
                                Element insertElem = linkDoc.GetElement(insertId);
                                if (insertElem == null) continue;

                                string insertDesignation = GetElementDesignation(insertElem, linkedParamName);
                                if (string.IsNullOrWhiteSpace(insertDesignation))
                                {
                                    insertDesignation = insertElem.Name ?? insertElem.Category?.Name ?? "Окно/Дверь";
                                }

                                (double insertWidthM, double insertHeightM, double insertAreaSqM) = GetInsertDimensions(insertElem);
                                if (insertAreaSqM > 0.001)
                                {
                                    insertsTotalAreaSqM += insertAreaSqM;
                                    double kVal = GetThermalTransmittanceK(insertElem);

                                    boundaryItems.Add(new HeatLossBoundaryItem
                                    {
                                        SpaceId = space.Id,
                                        SpaceNumber = space.Number ?? "",
                                        SpaceName = space.Name ?? "",
                                        OutdoorTemp = outdoorTemp,
                                        IndoorTemp = 20.0,
                                        Designation = insertDesignation,
                                        Orientation = "СЗ",
                                        LengthMeters = insertWidthM,
                                        HeightMeters = insertHeightM,
                                        AreaSqMeters = insertAreaSqM,
                                        CoeffN = 1.0,
                                        CoeffK = kVal,
                                        B1 = 0.1,
                                        B2 = 0.0,
                                        BoundingElementId = insertElem.Id,
                                        BoundingCategoryName = insertElem.Category?.Name ?? "Элемент"
                                    });
                                }
                            }
                        }

                        double netAreaSqM = Math.Max(0.0, areaSqM - insertsTotalAreaSqM);
                        if (netAreaSqM > 0.001)
                        {
                            double heightM = spaceHeightMeters;
                            double lengthM = heightM > 0 ? netAreaSqM / heightM : Math.Sqrt(netAreaSqM);
                            double kVal = GetThermalTransmittanceK(boundingElement);

                            boundaryItems.Add(new HeatLossBoundaryItem
                            {
                                SpaceId = space.Id,
                                SpaceNumber = space.Number ?? "",
                                SpaceName = space.Name ?? "",
                                OutdoorTemp = outdoorTemp,
                                IndoorTemp = 20.0,
                                Designation = wallDesignation,
                                Orientation = "СЗ",
                                LengthMeters = lengthM,
                                HeightMeters = heightM,
                                AreaSqMeters = netAreaSqM,
                                CoeffN = 1.0,
                                CoeffK = kVal,
                                B1 = 0.1,
                                B2 = 0.0,
                                BoundingElementId = boundingElement.Id,
                                BoundingCategoryName = boundingElement.Category?.Name ?? "Элемент"
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore individual space geometry errors
            }

            return boundaryItems;
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
            double rVal = GetParamDoubleValue(element,
                "ADSK_Сопротивление_теплопередаче",
                "Сопротивление_теплопередаче",
                "R_сопротивление",
                "R");

            if (rVal > 0.0001)
            {
                return 1.0 / rVal;
            }

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
                "BIMBCC_Обозначение",
                "ADSK_Марка",
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
            SetCubeParamValue(cube, item.SpaceNumber, "BIMBCC_Номер помещения", "ADSK_Номер помещения", "ADSK_Номер пространства", "Номер помещения", "Номер пространства", "Номер");

            // 2. Температура наружного воздуха
            SetCubeParamValue(cube, item.OutdoorTemp, "BIMBCC_Температура наружного воздуха", "ADSK_Температура наружного воздуха", "Температура наружного воздуха", "t_ext");

            // 3. Температура помещения
            SetCubeParamValue(cube, item.IndoorTemp, "BIMBCC_Температура помещения", "ADSK_Температура помещения", "Температура помещения", "t_int");

            // 4. Наименование помещения
            SetCubeParamValue(cube, item.SpaceName, "BIMBCC_Имя помещения", "ADSK_Имя помещения", "ADSK_Имя пространства", "Имя помещения", "Имя пространства", "Наименование", "Имя");

            // 5. Обозначение ограждающей конструкции
            SetCubeParamValue(cube, item.Designation, userDesignationParam, "BIMBCC_Обозначение", "ADSK_Обозначение", "ADSK_Марка", "Обозначение");

            // 6. Ориентация
            SetCubeParamValue(cube, item.Orientation, "BIMBCC_Ориентация", "ADSK_Ориентация", "Ориентация");

            // 7. Длина конструкции
            SetCubeParamValue(cube, item.LengthMeters, "BIMBCC_Длина", "ADSK_Длина", "Длина");

            // 8. Высота конструкции
            SetCubeParamValue(cube, item.HeightMeters, "BIMBCC_Высота", "ADSK_Высота", "Высота");

            // 9. Площадь
            SetCubeParamValue(cube, item.AreaSqMeters, userAreaParam, "BIMBCC_Площадь", "ADSK_Площадь", "Площадь", "ADSK_Значение");

            // 10. Коэффициент n
            SetCubeParamValue(cube, item.CoeffN, "BIMBCC_Коэффициент_n", "ADSK_Коэффициент_n", "Коэффициент_n", "n");

            // 11. Коэффициент теплопередачи k
            SetCubeParamValue(cube, item.CoeffK, "BIMBCC_Коэффициент_теплопередачи", "ADSK_Коэффициент_теплопередачи", "Коэффициент_теплопередачи", "k");

            // 12. b1
            SetCubeParamValue(cube, item.B1, "BIMBCC_b1", "ADSK_b1", "b1");

            // 13. b2
            SetCubeParamValue(cube, item.B2, "BIMBCC_b2", "ADSK_b2", "b2");

            // 14. Коэффициент надбавки
            SetCubeParamValue(cube, item.CoeffAllowance, "BIMBCC_Коэффициент_надбавки", "ADSK_Коэффициент_надбавки", "Коэффициент_надбавки", "Надбавка");

            // 15. Теплопотери (Вт)
            SetCubeParamValue(cube, item.HeatLossWatts, "BIMBCC_Теплопотери", "ADSK_Теплопотери", "Теплопотери", "Q");
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
                        if (name.Contains("Площадь"))
                        {
                            p.Set(UnitUtils.ConvertToInternalUnits(doubleValue, UnitTypeId.SquareMeters));
                        }
                        else if (name.Contains("Длина") || name.Contains("Высота"))
                        {
                            p.Set(UnitUtils.ConvertToInternalUnits(doubleValue, UnitTypeId.Meters));
                        }
                        else
                        {
                            p.Set(doubleValue);
                        }
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

        private ViewSchedule CreateOrUpdateRevitSchedule(string targetDesignationParamName, string targetAreaParamName, HeatLossCalculationResult result)
        {
            try
            {
                string scheduleName = "Спецификация ограждающих конструкций (Теплопотери)";

                ViewSchedule schedule = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

                if (schedule == null)
                {
                    schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    schedule.Name = scheduleName;
                }

                ScheduleDefinition definition = schedule.Definition;

                // Clear existing fields from schedule definition
                int existingFieldCount = definition.GetFieldCount();
                for (int f = existingFieldCount - 1; f >= 0; f--)
                {
                    try
                    {
                        definition.RemoveField(definition.GetField(f).FieldId);
                    }
                    catch { }
                }

                definition.ClearSortGroupFields();

                var schedulableFields = definition.GetSchedulableFields();

                string[][] columnCandidates = new string[][]
                {
                    new string[] { "BIMBCC_Номер помещения", "ADSK_Номер помещения", "Номер помещения" },
                    new string[] { "BIMBCC_Температура наружного воздуха", "ADSK_Температура наружного воздуха", "Температура наружного воздуха" },
                    new string[] { "BIMBCC_Температура помещения", "ADSK_Температура помещения", "Температура помещения" },
                    new string[] { "BIMBCC_Имя помещения", "ADSK_Имя помещения", "Имя помещения", "ADSK_Имя пространства", "Имя пространства" },
                    new string[] { targetDesignationParamName, "BIMBCC_Обозначение", "ADSK_Обозначение", "Обозначение" },
                    new string[] { "BIMBCC_Ориентация", "ADSK_Ориентация", "Ориентация" },
                    new string[] { "BIMBCC_Длина", "ADSK_Длина", "Длина" },
                    new string[] { "BIMBCC_Высота", "ADSK_Высота", "Высота" },
                    new string[] { targetAreaParamName, "BIMBCC_Площадь", "ADSK_Площадь", "Площадь" },
                    new string[] { "BIMBCC_Коэффициент_n", "ADSK_Коэффициент_n", "Коэффициент_n" },
                    new string[] { "BIMBCC_Коэффициент_теплопередачи", "ADSK_Коэффициент_теплопередачи", "Коэффициент_теплопередачи" },
                    new string[] { "BIMBCC_b1", "ADSK_b1", "b1" },
                    new string[] { "BIMBCC_b2", "ADSK_b2", "b2" },
                    new string[] { "BIMBCC_Коэффициент_надбавки", "ADSK_Коэффициент_надбавки", "Коэффициент_надбавки" },
                    new string[] { "BIMBCC_Теплопотери", "ADSK_Теплопотери", "Теплопотери" }
                };

                ScheduleField fieldSpaceNumber = null;
                ScheduleField fieldSpaceName = null;
                ScheduleField fieldDesignation = null;
                int addedFieldsCount = 0;

                for (int c = 0; c < columnCandidates.Length; c++)
                {
                    var candidates = columnCandidates[c];
                    SchedulableField matchedSf = null;

                    foreach (var sf in schedulableFields)
                    {
                        string sfName = sf.GetName(_doc).Trim();
                        if (candidates.Any(cand => !string.IsNullOrEmpty(cand) && sfName.Equals(cand, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchedSf = sf;
                            break;
                        }

                        if (sf.ParameterId != ElementId.InvalidElementId)
                        {
                            Element elem = _doc.GetElement(sf.ParameterId);
                            if (elem != null && candidates.Any(cand => !string.IsNullOrEmpty(cand) && elem.Name.Equals(cand, StringComparison.OrdinalIgnoreCase)))
                            {
                                matchedSf = sf;
                                break;
                            }
                        }
                    }

                    if (matchedSf != null)
                    {
                        ScheduleField field = definition.AddField(matchedSf);
                        addedFieldsCount++;

                        if (c == 0) fieldSpaceNumber = field;
                        if (c == 3) fieldSpaceName = field;
                        if (c == 4) fieldDesignation = field;

                        if (c == 8 || c == 14) // Area or HeatLoss
                        {
                            field.DisplayType = ScheduleFieldDisplayType.Totals;
                        }
                    }
                    else
                    {
                        result?.Logs.Add($"Столбец #{c + 1} ({candidates[0]}): не найден в доступных полях.");
                    }
                }

                result?.Logs.Add($"В спецификацию добавлено полей: {addedFieldsCount} из 15.");

                if (fieldSpaceNumber != null)
                {
                    ScheduleSortGroupField sortNumber = new ScheduleSortGroupField(fieldSpaceNumber.FieldId);
                    sortNumber.ShowHeader = true;
                    sortNumber.ShowBlankLine = true;
                    definition.AddSortGroupField(sortNumber);
                }

                if (fieldSpaceName != null)
                {
                    ScheduleSortGroupField sortName = new ScheduleSortGroupField(fieldSpaceName.FieldId);
                    sortName.ShowHeader = false;
                    definition.AddSortGroupField(sortName);
                }

                if (fieldDesignation != null)
                {
                    ScheduleSortGroupField sortDesig = new ScheduleSortGroupField(fieldDesignation.FieldId);
                    definition.AddSortGroupField(sortDesig);
                }

                return schedule;
            }
            catch (Exception ex)
            {
                result?.Logs.Add($"Ошибка формирования спецификации: {ex.Message}");
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
                sb.AppendLine("1. Номер помещения;2. Температура наружного воздуха (°C);3. Температура помещения (°C);4. Наименование помещения;5. Обозначение конструкции;6. Ориентация;7. Длина (м);8. Высота (м);9. Площадь (м²);10. Коэффициент n;11. Коэффициент k;12. b1;13. b2;14. Коэффициент надбавки;15. Теплопотери (Вт)");

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
