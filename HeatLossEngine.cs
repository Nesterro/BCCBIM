using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;


namespace BCCPlugIn
{
    /// <summary>
    /// Основной движок модуля «Теплопотери».
    /// Для каждого MEP-пространства (Space) находит все ограждающие конструкции,
    /// размещает экземпляр семейства-кубика и заполняет 18 параметров.
    /// </summary>
    public class HeatLossEngine
    {
        // ── Имена параметров ────────────────────────────────────────────────
        public const string P_ROOM_NUMBER       = "BCC_HL_Номер помещения";
        public const string P_ROOM_NAME         = "BCC_HL_Имя помещения";
        public const string P_TEMP_OUT          = "BCC_HL_Температура наружного воздуха";
        public const string P_TEMP_IN           = "BCC_HL_Температура внутреннего воздуха";
        public const string P_CORNER_TYPE       = "BCC_HL_Тип углового помещения";
        public const string P_CONSTR_LABEL      = "BCC_HL_Обозначение конструкции";
        public const string P_ORIENTATION       = "BCC_HL_Ориентация конструкции";
        public const string P_LENGTH            = "BCC_HL_Длина конструкции";
        public const string P_HEIGHT            = "BCC_HL_Высота конструкции";
        public const string P_AREA              = "BCC_HL_Площадь конструкции";
        public const string P_COEFF_N           = "BCC_HL_Коэффициент n";
        public const string P_COEFF_K           = "BCC_HL_Коэффициент теплопередачи k";
        public const string P_ADD_B1            = "BCC_HL_Надбавка b1";
        public const string P_ADD_B2            = "BCC_HL_Надбавка b2";
        public const string P_ADD_B3            = "BCC_HL_Надбавка b3";
        public const string P_ADD_B4            = "BCC_HL_Надбавка b4";
        public const string P_COEFF_ADD         = "BCC_HL_Коэффициент надбавки";
        public const string P_HEAT_LOSS         = "BCC_HL_Теплопотери";

        private static readonly string[] AllTextParams = new[]
        {
            P_ROOM_NUMBER, P_ROOM_NAME, P_CORNER_TYPE,
            P_CONSTR_LABEL, P_ORIENTATION
        };
        private static readonly string[] AllNumberParams = new[]
        {
            P_TEMP_OUT, P_TEMP_IN, P_LENGTH, P_HEIGHT, P_AREA,
            P_COEFF_N, P_COEFF_K, P_ADD_B1, P_ADD_B2, P_ADD_B3,
            P_ADD_B4, P_COEFF_ADD, P_HEAT_LOSS
        };

        private readonly Document _doc;

        public HeatLossEngine(Document doc)
        {
            _doc = doc;
        }

        private Dictionary<ElementId, string> _typeCodeMap;
        private Dictionary<string, int> _prefixCounterMap;

        // ───────────────────────────────────────────────────────────────────
        // Главный метод
        // ───────────────────────────────────────────────────────────────────
        public int Run(
            List<Space> spaces,
            FamilySymbol symbol,
            double tempOutside,
            double tempInside,
            bool processExteriorWalls,
            bool processInteriorWalls,
            bool processFloors,
            bool processDoors,
            bool processWindows)
        {
            int placedCount = 0;
            _typeCodeMap = new Dictionary<ElementId, string>();
            _prefixCounterMap = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(_doc, "BIMBCC Теплопотери — добавление параметров"))
            {
                tx.Start();
                EnsureProjectParameters();
                tx.Commit();
            }

            using (Transaction tx = new Transaction(_doc, "BIMBCC Теплопотери — расстановка кубиков"))
            {
                tx.Start();

                // Удалить старые кубики теплопотерь перед новой расстановкой
                try
                {
                    FilteredElementCollector oldCubeCollector = new FilteredElementCollector(_doc);
                    List<ElementId> oldCubeIds = oldCubeCollector
                        .OfClass(typeof(FamilyInstance))
                        .OfCategory(BuiltInCategory.OST_GenericModel)
                        .WhereElementIsNotElementType()
                        .Where(e => {
                            Parameter pMark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                            return (pMark != null && pMark.AsString() != null && pMark.AsString().StartsWith("BCC_HL")) ||
                                   e.LookupParameter("BCC_HL_Теплопотери") != null;
                        })
                        .Select(e => e.Id)
                        .ToList();

                    if (oldCubeIds.Count > 0)
                    {
                        _doc.Delete(oldCubeIds);
                    }
                }
                catch { }

                // Активировать символ
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    _doc.Regenerate();
                }

                SpatialElementBoundaryOptions boundaryOpts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };

                // Построить карту количества пространств, примыкающих к каждому ограждению
                Dictionary<string, int> wallSpaceCountMap = BuildWallSpaceCountMap(spaces, boundaryOpts);

                foreach (Space space in spaces)
                {
                    string roomNumber, roomName;
                    GetSpaceRoomNumberAndName(space, out roomNumber, out roomName);
                    double roomHeight = space.UnboundedHeight; // футы

                    IList<IList<BoundarySegment>> boundaries =
                        space.GetBoundarySegments(boundaryOpts);

                    // Предварительный сканирование ориентаций наружных стен для определения углового помещения
                    HashSet<string> exteriorOrientations = new HashSet<string>();
                    foreach (IList<BoundarySegment> loop in boundaries)
                    {
                        foreach (BoundarySegment seg in loop)
                        {
                            ElementId boundElemId = seg.ElementId;
                            if (boundElemId == ElementId.InvalidElementId) continue;

                            Element boundElem = null;
                            Element hostElem = _doc.GetElement(boundElemId);
                            if (hostElem is RevitLinkInstance rvtLink)
                            {
                                Document linkedDoc = rvtLink.GetLinkDocument();
                                if (linkedDoc != null && seg.LinkElementId != null && seg.LinkElementId != ElementId.InvalidElementId)
                                {
                                    boundElem = linkedDoc.GetElement(seg.LinkElementId);
                                }
                            }
                            else
                            {
                                boundElem = hostElem;
                            }

                            BuiltInCategory cat = boundElem != null ? GetBuiltInCategory(boundElem) : BuiltInCategory.OST_Walls;
                            if (cat == BuiltInCategory.OST_Walls)
                            {
                                string key = (seg.LinkElementId != null && seg.LinkElementId != ElementId.InvalidElementId)
                                    ? seg.ElementId.IntegerValue.ToString() + "_" + seg.LinkElementId.IntegerValue.ToString()
                                    : seg.ElementId.IntegerValue.ToString();

                                bool isInterior = IsInteriorWall(boundElem, key, wallSpaceCountMap);
                                if (!isInterior)
                                {
                                    string o = GetOrientation(boundElem, cat, seg);
                                    if (!string.IsNullOrEmpty(o) && o != "Горизонтальная")
                                    {
                                        exteriorOrientations.Add(o);
                                    }
                                }
                            }
                        }
                    }

                    bool isCornerSpace = (exteriorOrientations.Count >= 2);
                    string cornerTypeStr = isCornerSpace ? "Угловое" : "Обычное";

                    // Собрать уникальные элементы-ограждения
                    HashSet<string> processedKeys = new HashSet<string>();

                    foreach (IList<BoundarySegment> loop in boundaries)
                    {
                        int loopCount = loop.Count;
                        for (int i = 0; i < loopCount; i++)
                        {
                            BoundarySegment seg = loop[i];
                            BoundarySegment prevSeg = loop[(i - 1 + loopCount) % loopCount];
                            BoundarySegment nextSeg = loop[(i + 1) % loopCount];

                            ElementId boundElemId = seg.ElementId;
                            if (boundElemId == ElementId.InvalidElementId) continue;

                            Element boundElem = null;
                            RevitLinkInstance linkInst = null;

                            Element hostElem = _doc.GetElement(boundElemId);
                            if (hostElem is RevitLinkInstance rvtLink)
                            {
                                linkInst = rvtLink;
                                Document linkedDoc = rvtLink.GetLinkDocument();
                                if (linkedDoc != null)
                                {
                                    try
                                    {
                                        ElementId linkedElemId = seg.LinkElementId;
                                        if (linkedElemId != null && linkedElemId != ElementId.InvalidElementId)
                                        {
                                            boundElem = linkedDoc.GetElement(linkedElemId);
                                        }
                                    }
                                    catch { }
                                }
                            }
                            else
                            {
                                boundElem = hostElem;
                            }

                            // Если элемент равен null (например, разделитель помещений), вычисляем категорию как Wall по умолчанию
                            BuiltInCategory cat = boundElem != null ? GetBuiltInCategory(boundElem) : BuiltInCategory.OST_Walls;

                            bool isWall    = (cat == BuiltInCategory.OST_Walls);
                            bool isFloor   = (cat == BuiltInCategory.OST_Floors  ||
                                              cat == BuiltInCategory.OST_Ceilings ||
                                              cat == BuiltInCategory.OST_StructuralFoundation ||
                                              cat == BuiltInCategory.OST_Roofs);
                            bool isDoor    = (cat == BuiltInCategory.OST_Doors);
                            bool isWindow  = (cat == BuiltInCategory.OST_Windows);

                            string wallKey = (seg.LinkElementId != null && seg.LinkElementId != ElementId.InvalidElementId)
                                ? seg.ElementId.IntegerValue.ToString() + "_" + seg.LinkElementId.IntegerValue.ToString()
                                : seg.ElementId.IntegerValue.ToString();

                            bool isInteriorWall = IsInteriorWall(boundElem, wallKey, wallSpaceCountMap);

                            if (isWall)
                            {
                                if (!isInteriorWall && !processExteriorWalls) continue;
                                if (isInteriorWall  && !processInteriorWalls) continue;
                            }

                            if (isFloor  && !processFloors)  continue;
                            if (isDoor   && !processDoors)   continue;
                            if (isWindow && !processWindows) continue;

                            if (!isWall && !isFloor && !isDoor && !isWindow) continue;

                            // Ключ уникальности (с учётом линка)
#pragma warning disable CS0618
                            string uniqueKey = (linkInst != null ? linkInst.Id.IntegerValue.ToString() + "_" : "") +
                                               (boundElem != null ? boundElem.Id.IntegerValue.ToString() : seg.GetCurve().Evaluate(0.5, true).ToString());
#pragma warning restore CS0618
                            if (!processedKeys.Add(uniqueKey)) continue;

                            // Вычислить точку размещения в координатах основной модели
                            XYZ placementPoint = GetPlacementPoint(boundElem, linkInst, space, seg, roomHeight);
                            if (placementPoint == null) continue;

                            // Разместить экземпляр
                            FamilyInstance inst = _doc.Create.NewFamilyInstance(
                                placementPoint,
                                symbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (inst == null) continue;

                            // Вычислить геометрические характеристики с припуском до осей смежных стен
                            double lengthMm  = 0;
                            double heightMm  = 0;
                            double areaSqM   = 0;
                            string label     = GetConstructionLabel(boundElem, cat);
                            string orient    = GetOrientation(boundElem, cat, seg);

                            if (boundElem != null)
                            {
                                GetConstructionDimensions(boundElem, cat, seg, prevSeg, nextSeg, loop, i, roomHeight,
                                    out lengthMm, out heightMm, out areaSqM);
                            }
                            else
                            {
                                Element prevElem = GetElementFromSegment(prevSeg);
                                Element nextElem = GetElementFromSegment(nextSeg);
                                double tPrevFt = GetWallThicknessFt(prevElem);
                                double tNextFt = GetWallThicknessFt(nextElem);

                                double calcLengthFt = GetSpaceCroppedWallLengthFt(seg, loop, i, tPrevFt, tNextFt);
                                lengthMm = calcLengthFt * 304.8;
                                heightMm = roomHeight * 304.8;
                                areaSqM  = (lengthMm / 1000.0) * (heightMm / 1000.0);
                            }

                            // Расчёт надбавок
                            bool isExteriorElem = (isWall || isDoor || isWindow);
                            double b1 = GetB1OrientationAddon(orient);
                            double b2 = (isCornerSpace && isExteriorElem) ? 0.05 : 0.00;
                            double b3 = 0.00;
                            double b4 = 0.00;
                            double coeffAdd = 1.0 + b1 + b2 + b3 + b4;

                            // Заполнить параметры
                            SetText(inst, P_ROOM_NUMBER,  roomNumber);
                            SetText(inst, P_ROOM_NAME,    roomName);
                            SetText(inst, P_CONSTR_LABEL, label);
                            SetText(inst, P_ORIENTATION,  orient);
                            SetText(inst, P_CORNER_TYPE,  cornerTypeStr);

                            SetNumber(inst, P_TEMP_OUT,  tempOutside);
                            SetNumber(inst, P_TEMP_IN,   tempInside);
                            SetNumber(inst, P_LENGTH,    lengthMm);
                            SetNumber(inst, P_HEIGHT,    heightMm);
                            SetNumber(inst, P_AREA,      areaSqM);
                            SetNumber(inst, P_COEFF_N,   1); // Коэффициент n по умолчанию 1
                            SetNumber(inst, P_COEFF_K,   0);
                            SetNumber(inst, P_ADD_B1,    b1); // Надбавка b1 по ориентации
                            SetNumber(inst, P_ADD_B2,    b2); // Надбавка b2 (угловое помещение)
                            SetNumber(inst, P_ADD_B3,    b3);
                            SetNumber(inst, P_ADD_B4,    b4);
                            SetNumber(inst, P_COEFF_ADD, coeffAdd); // Коэффициент надбавки = 1 + b1 + b2 + b3 + b4
                            SetNumber(inst, P_HEAT_LOSS, 0);

                            placedCount++;

                            // Если обрабатываемый элемент — стена, и включена обработка дверей/окон,
                            // ищем расположенные в ней двери и окна
                            if (boundElem != null && isWall && (processDoors || processWindows))
                            {
                                ProcessWallOpenings(boundElem, linkInst, space, roomNumber, roomName, cornerTypeStr, isCornerSpace,
                                                    tempOutside, tempInside, symbol, seg, roomHeight,
                                                    processDoors, processWindows, processedKeys, ref placedCount);
                            }
                        }
                    }
                }

                tx.Commit();
            }

            return placedCount;
        }

        private static double GetB1OrientationAddon(string orient)
        {
            if (string.IsNullOrEmpty(orient)) return 0.0;

            switch (orient.Trim().ToUpperInvariant())
            {
                case "С":
                case "СВ":
                case "В":
                case "СЗ":
                    return 0.10; // +10%

                case "З":
                case "ЮВ":
                    return 0.05; // +5%

                case "Ю":
                case "ЮЗ":
                case "ГОРИЗОНТАЛЬНАЯ":
                default:
                    return 0.00;
            }
        }

        private void GetSpaceRoomNumberAndName(Space space, out string roomNumber, out string roomName)
        {
            roomNumber = "";
            roomName = "";

            // 1. Попытка через BuiltInParameter связанных помещений
            try
            {
                Parameter pNum = space.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NUMBER);
                if (pNum != null && pNum.HasValue) roomNumber = pNum.AsString();
            }
            catch { }

            try
            {
                Parameter pName = space.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NAME);
                if (pName != null && pName.HasValue) roomName = pName.AsString();
            }
            catch { }

            // 2. Попытка через LookupParameter (стандартные параметры сопоставления)
            if (string.IsNullOrWhiteSpace(roomNumber))
            {
                Parameter p = space.LookupParameter("Номер помещения") ?? space.LookupParameter("Номер связанного помещения");
                if (p != null && p.HasValue) roomNumber = p.AsString();
            }
            if (string.IsNullOrWhiteSpace(roomName))
            {
                Parameter p = space.LookupParameter("Имя помещения") ?? space.LookupParameter("Имя связанного помещения");
                if (p != null && p.HasValue) roomName = p.AsString();
            }

            // 3. Фолбэк на собственные номер/имя пространства
            if (string.IsNullOrWhiteSpace(roomNumber))
            {
                roomNumber = space.Number;
            }
            if (string.IsNullOrWhiteSpace(roomName))
            {
                roomName = space.Name;
            }

            if (roomNumber == null) roomNumber = "";
            if (roomName == null) roomName = "";
        }

        // ───────────────────────────────────────────────────────────────────
        // Создание / обновление спецификации
        // ───────────────────────────────────────────────────────────────────
        public string CreateOrUpdateSchedule(out string errorMessage)
        {
            const string schedName = "BIMBCC Теплопотери";
            errorMessage = null;

            try
            {
                using (Transaction tx = new Transaction(_doc, "BIMBCC Теплопотери — спецификация"))
                {
                    tx.Start();

                    // Удалить старую, если есть
                    ViewSchedule existing = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .FirstOrDefault(vs => vs.Name == schedName);
                    if (existing != null)
                        _doc.Delete(existing.Id);

                    // Создать новую
                    ViewSchedule sched = ViewSchedule.CreateSchedule(
                        _doc,
                        new ElementId(BuiltInCategory.OST_GenericModel));
                    sched.Name = schedName;

                    ScheduleDefinition def = sched.Definition;
                    def.IsItemized = false; // Снять галочку "Для каждого экземпляра"

                    // Добавить поля в порядке столбцов
                    string[] fieldOrder = new[]
                    {
                        P_ROOM_NUMBER, P_ROOM_NAME, P_TEMP_OUT, P_TEMP_IN,
                        P_CORNER_TYPE, P_CONSTR_LABEL, P_ORIENTATION,
                        P_LENGTH, P_HEIGHT, P_AREA,
                        P_COEFF_N, P_COEFF_K,
                        P_ADD_B1, P_ADD_B2, P_ADD_B3, P_ADD_B4,
                        P_COEFF_ADD, P_HEAT_LOSS
                    };

                    // Собрать доступные поля
                    IList<SchedulableField> schedulable = def.GetSchedulableFields();

                    foreach (string paramName in fieldOrder)
                    {
                        SchedulableField sf = schedulable.FirstOrDefault(f => f.GetName(_doc) == paramName);
                        if (sf != null)
                        {
                            ScheduleField addedField = def.AddField(sf);
                            if (paramName == P_AREA || paramName == P_HEAT_LOSS)
                            {
                                addedField.DisplayType = ScheduleFieldDisplayType.Totals; // Вычисление итогов по площади и теплопотерям!
                            }
                        }
                    }

                    // Сортировка: Номер (с заголовком) → Имя помещения → Обозначение конструкции → Площадь конструкции
                    def.ClearSortGroupFields();

                    ScheduleField fieldRoomNum = null;
                    ScheduleField fieldRoomName = null;
                    ScheduleField fieldConstrLabel = null;
                    ScheduleField fieldArea = null;

                    foreach (ScheduleField f in def.GetFieldOrder().Select(id => def.GetField(id)))
                    {
                        string fn = f.GetSchedulableField().GetName(_doc);
                        if (fn == P_ROOM_NUMBER) fieldRoomNum = f;
                        else if (fn == P_ROOM_NAME) fieldRoomName = f;
                        else if (fn == P_CONSTR_LABEL) fieldConstrLabel = f;
                        else if (fn == P_AREA) fieldArea = f;
                    }

                    // 1. По номеру помещения с заголовком
                    if (fieldRoomNum != null)
                    {
                        ScheduleSortGroupField sort1 = new ScheduleSortGroupField(fieldRoomNum.FieldId);
                        sort1.ShowHeader = true; // С заголовком!
                        def.AddSortGroupField(sort1);
                    }

                    // 2. По имени помещения
                    if (fieldRoomName != null)
                    {
                        ScheduleSortGroupField sort2 = new ScheduleSortGroupField(fieldRoomName.FieldId);
                        def.AddSortGroupField(sort2);
                    }

                    // 3. По обозначению конструкции
                    if (fieldConstrLabel != null)
                    {
                        ScheduleSortGroupField sort3 = new ScheduleSortGroupField(fieldConstrLabel.FieldId);
                        def.AddSortGroupField(sort3);
                    }

                    // 4. По площади конструкции
                    if (fieldArea != null)
                    {
                        ScheduleSortGroupField sort4 = new ScheduleSortGroupField(fieldArea.FieldId);
                        def.AddSortGroupField(sort4);
                    }

                    tx.Commit();
                }

                return schedName;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine(
                    $"[HeatLossEngine] CreateOrUpdateSchedule failed: {ex.Message}");
                return null;
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Сбор уникальных типов конструкций для задания коэффициентов k
        // ───────────────────────────────────────────────────────────────────
        public List<HeatLossCoeffItem> GetPlacedConstructionTypes()
        {
            List<FamilyInstance> cubes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter(P_CONSTR_LABEL) != null)
                .ToList();

            Dictionary<string, string> uniqueTypes = new Dictionary<string, string>();

            foreach (var cube in cubes)
            {
                string label = GetText(cube, P_CONSTR_LABEL);
                if (string.IsNullOrEmpty(label)) continue;

                if (!uniqueTypes.ContainsKey(label))
                {
                    string name = GetFriendlyTypeName(label);
                    uniqueTypes[label] = name;
                }
            }

            return uniqueTypes
                .Select(kvp => new HeatLossCoeffItem
                {
                    Code = kvp.Key,
                    Name = kvp.Value,
                    CoeffK = GetDefaultCoeffK(kvp.Key)
                })
                .OrderBy(item => item.Code)
                .ToList();
        }

        private static string GetFriendlyTypeName(string code)
        {
            if (code.StartsWith("НС")) return "Наружная стена";
            if (code.StartsWith("ВС")) return "Внутренняя стена";
            if (code.StartsWith("ОК")) return "Окно";
            if (code.StartsWith("ДВ")) return "Дверь";
            if (code.StartsWith("ПР")) return "Перекрытие / Пол";
            if (code.StartsWith("ПОТ")) return "Потолок";
            if (code.StartsWith("КР")) return "Кровля / Крыша";
            return "Ограждающая конструкция";
        }

        private static double GetDefaultCoeffK(string code)
        {
            if (code.StartsWith("НС")) return 0.35;
            if (code.StartsWith("ВС")) return 0.60;
            if (code.StartsWith("ОК")) return 1.30;
            if (code.StartsWith("ДВ")) return 1.80;
            if (code.StartsWith("ПР")) return 0.45;
            if (code.StartsWith("ПОТ")) return 0.50;
            if (code.StartsWith("КР")) return 0.25;
            return 1.0;
        }

        // ───────────────────────────────────────────────────────────────────
        // Вторая транзакция: Запись k и расчет Q = (t_in - t_out)*S*n*k*CoeffAdd
        // ───────────────────────────────────────────────────────────────────
        public int ApplyCoefficientsAndCalculateHeatLoss(Dictionary<string, double> coeffKMap)
        {
            List<FamilyInstance> cubes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter(P_CONSTR_LABEL) != null)
                .ToList();

            int updatedCount = 0;

            using (Transaction tx = new Transaction(_doc, "BIMBCC | Запись коэффициентов и расчёт теплопотерь"))
            {
                tx.Start();

                foreach (FamilyInstance cube in cubes)
                {
                    string label = GetText(cube, P_CONSTR_LABEL);
                    if (string.IsNullOrEmpty(label)) continue;

                    if (coeffKMap.TryGetValue(label, out double coeffK))
                    {
                        // 1. Записать коэффициент теплопередачи k
                        SetNumber(cube, P_COEFF_K, coeffK);

                        // 2. Считать параметры для расчёта Q
                        double tOut = GetNumber(cube, P_TEMP_OUT);
                        double tIn = GetNumber(cube, P_TEMP_IN);
                        double area = GetNumber(cube, P_AREA);
                        double n = GetNumber(cube, P_COEFF_N);
                        double coeffAdd = GetNumber(cube, P_COEFF_ADD);

                        // Формула: Q = (t_in - t_out) * Area * n * k * CoeffAdd
                        double deltaT = tIn - tOut;
                        double qHeatLoss = deltaT * area * n * coeffK * coeffAdd;

                        // 3. Записать итоговые теплопотери Q (Вт)
                        SetNumber(cube, P_HEAT_LOSS, Math.Round(qHeatLoss, 2));

                        updatedCount++;
                    }
                }

                tx.Commit();
            }

            return updatedCount;
        }

        private static double GetNumber(Element elem, string paramName)
        {
            Parameter p = elem.LookupParameter(paramName);
            if (p != null && p.HasValue)
            {
                if (p.StorageType == StorageType.Double)
                    return p.AsDouble();
                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                    return val;
            }
            return 0.0;
        }

        // ───────────────────────────────────────────────────────────────────
        // Добавление параметров проекта в категорию OST_GenericModel
        // ───────────────────────────────────────────────────────────────────
        private void EnsureProjectParameters()
        {
            CategorySet catSet = _doc.Application.Create.NewCategorySet();
            Category genericCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
            catSet.Insert(genericCat);

            InstanceBinding instanceBinding =
                _doc.Application.Create.NewInstanceBinding(catSet);

            BindingMap bindMap = _doc.ParameterBindings;

            // Текстовые параметры
            foreach (string name in AllTextParams)
                EnsureParam(bindMap, instanceBinding, name,
                    SpecTypeId.String.Text, GroupTypeId.Data);

            // Числовые параметры
            foreach (string name in AllNumberParams)
                EnsureParam(bindMap, instanceBinding, name,
                    SpecTypeId.Number, GroupTypeId.Data);
        }

        private void EnsureParam(
            BindingMap bindMap,
            InstanceBinding binding,
            string paramName,
            ForgeTypeId specTypeId,
            ForgeTypeId groupTypeId)
        {
            // Проверить, не существует ли уже
            DefinitionBindingMapIterator it = bindMap.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key.Name == paramName) return; // уже есть
            }

            // Revit 2024: создаём параметр проекта через временный файл ФОП
            string originalSharedParamFile = _doc.Application.SharedParametersFilename;
            string tempFile = System.IO.Path.GetTempFileName();

            try
            {
                // Инициализируем пустой временный файл ФОП
                System.IO.File.WriteAllText(tempFile,
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually.\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "GROUP\t1\tBIMBCC\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");

                _doc.Application.SharedParametersFilename = tempFile;
                DefinitionFile defFile = _doc.Application.OpenSharedParameterFile();

                // Создаём группу и определение
                DefinitionGroup grp = defFile.Groups.get_Item("BIMBCC")
                                   ?? defFile.Groups.Create("BIMBCC");

                ExternalDefinitionCreationOptions extOpts =
                    new ExternalDefinitionCreationOptions(paramName, specTypeId)
                    {
                        UserModifiable = true
                    };

                ExternalDefinition extDef = grp.Definitions.Create(extOpts) as ExternalDefinition;
                if (extDef == null) return;

                bindMap.Insert(extDef, binding, groupTypeId);
            }
            finally
            {
                // Восстанавливаем исходный файл ФОП
                try
                {
                    _doc.Application.SharedParametersFilename =
                        string.IsNullOrEmpty(originalSharedParamFile) ? "" : originalSharedParamFile;
                }
                catch { }

                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Вспомогательные: геометрия
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// <summary>
        /// Точка размещения кубика — центр грани ограждения.
        /// </summary>
        private XYZ GetPlacementPoint(
            Element elem, RevitLinkInstance linkInst, Space space, BoundarySegment seg, double roomHeightFt)
        {
            try
            {
                // 1. Для сегмента границы наиболее точно — середина сегмента на высоте 1/2 пространства
                if (seg != null)
                {
                    Curve c = seg.GetCurve();
                    if (c != null)
                    {
                        XYZ mid = c.Evaluate(0.5, true);
                        BoundingBoxXYZ sbb = space.get_BoundingBox(null);
                        double spaceBottomZ = sbb != null ? sbb.Min.Z : (space.Level != null ? space.Level.ProjectElevation : mid.Z);
                        return new XYZ(mid.X, mid.Y, spaceBottomZ + roomHeightFt / 2.0);
                    }
                }

                // 2. Фолбэк на BoundingBox элемента (с трансформацией связанной модели)
                if (elem != null)
                {
                    BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                    if (bb != null)
                    {
                        XYZ centerLocal = (bb.Min + bb.Max) / 2.0;
                        return linkInst != null ? linkInst.GetTotalTransform().OfPoint(centerLocal) : centerLocal;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void ProcessWallOpenings(
            Element wallElem,
            RevitLinkInstance linkInst,
            Space space,
            string roomNumber,
            string roomName,
            string cornerTypeStr,
            bool isCornerSpace,
            double tempOutside,
            double tempInside,
            FamilySymbol symbol,
            BoundarySegment seg,
            double roomHeightFt,
            bool processDoors,
            bool processWindows,
            HashSet<string> processedKeys,
            ref int placedCount)
        {
            try
            {
                Document doc = wallElem.Document;
                List<FamilyInstance> openings = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Host != null && fi.Host.Id == wallElem.Id)
                    .Where(fi =>
                    {
                        if (fi.Category == null) return false;
#pragma warning disable CS0618
                        long cId = fi.Category.Id.IntegerValue;
#pragma warning restore CS0618
                        bool isD = (cId == (long)BuiltInCategory.OST_Doors);
                        bool isW = (cId == (long)BuiltInCategory.OST_Windows);
                        return (isD && processDoors) || (isW && processWindows);
                    })
                    .ToList();

                if (openings.Count == 0) return;

                BoundingBoxXYZ spaceBbox = space.get_BoundingBox(null);
                Transform linkTransform = linkInst != null ? linkInst.GetTotalTransform() : Transform.Identity;

                foreach (FamilyInstance opening in openings)
                {
#pragma warning disable CS0618
                    string openingKey = (linkInst != null ? linkInst.Id.IntegerValue.ToString() + "_" : "") + opening.Id.IntegerValue.ToString();
#pragma warning restore CS0618
                    if (!processedKeys.Add(openingKey)) continue;

                    XYZ worldPt = null;
                    LocationPoint locPt = opening.Location as LocationPoint;
                    if (locPt != null)
                    {
                        worldPt = linkTransform.OfPoint(locPt.Point);
                    }
                    else
                    {
                        BoundingBoxXYZ obb = opening.get_BoundingBox(null);
                        if (obb != null)
                        {
                            XYZ centerLocal = (obb.Min + obb.Max) / 2.0;
                            worldPt = linkTransform.OfPoint(centerLocal);
                        }
                    }

                    if (worldPt == null) continue;

                    // Проверка близости проёма к габаритам пространства (с допуском 3.2 фута / 1 м)
                    if (spaceBbox != null)
                    {
                        double tol = 3.2;
                        if (worldPt.X < spaceBbox.Min.X - tol || worldPt.X > spaceBbox.Max.X + tol ||
                            worldPt.Y < spaceBbox.Min.Y - tol || worldPt.Y > spaceBbox.Max.Y + tol ||
                            worldPt.Z < spaceBbox.Min.Z - tol || worldPt.Z > spaceBbox.Max.Z + tol)
                        {
                            continue;
                        }
                    }

                    FamilyInstance inst = _doc.Create.NewFamilyInstance(
                        worldPt,
                        symbol,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                    if (inst != null)
                    {
                        BuiltInCategory openCat = GetBuiltInCategory(opening);
                        double lMm = 0, hMm = 0, aSqM = 0;
                        GetConstructionDimensions(opening, openCat, seg, null, null, null, 0, roomHeightFt, out lMm, out hMm, out aSqM);
                        string label = GetConstructionLabel(opening, openCat);
                        string orient = GetOrientation(opening, openCat, seg);

                        double b1 = GetB1OrientationAddon(orient);
                        double b2 = isCornerSpace ? 0.05 : 0.00;
                        double b3 = 0.00;
                        double b4 = 0.00;
                        double coeffAdd = 1.0 + b1 + b2 + b3 + b4;

                        SetText(inst, P_ROOM_NUMBER, roomNumber);
                        SetText(inst, P_ROOM_NAME, roomName);
                        SetText(inst, P_CONSTR_LABEL, label);
                        SetText(inst, P_ORIENTATION, orient);
                        SetText(inst, P_CORNER_TYPE, cornerTypeStr);

                        SetNumber(inst, P_TEMP_OUT, tempOutside);
                        SetNumber(inst, P_TEMP_IN, tempInside);
                        SetNumber(inst, P_LENGTH, lMm);
                        SetNumber(inst, P_HEIGHT, hMm);
                        SetNumber(inst, P_AREA, aSqM);
                        SetNumber(inst, P_COEFF_N, 1);
                        SetNumber(inst, P_COEFF_K, 0);
                        SetNumber(inst, P_ADD_B1, b1);
                        SetNumber(inst, P_ADD_B2, b2);
                        SetNumber(inst, P_ADD_B3, b3);
                        SetNumber(inst, P_ADD_B4, b4);
                        SetNumber(inst, P_COEFF_ADD, coeffAdd);
                        SetNumber(inst, P_HEAT_LOSS, 0);

                        placedCount++;
                    }
                }
            }
            catch { }
        }

        private void GetConstructionDimensions(
            Element elem,
            BuiltInCategory cat,
            BoundarySegment seg,
            BoundarySegment prevSeg,
            BoundarySegment nextSeg,
            IList<BoundarySegment> loop,
            int segIndex,
            double roomHeightFt,
            out double lengthMm,
            out double heightMm,
            out double areaSqM)
        {
            lengthMm = 0;
            heightMm = 0;
            areaSqM  = 0;

            const double ft2mm = 304.8;
            const double ft2m  = 0.3048;

            try
            {
                if (cat == BuiltInCategory.OST_Walls)
                {
                    Wall wall = elem as Wall;

                    Element prevElem = GetElementFromSegment(prevSeg);
                    Element nextElem = GetElementFromSegment(nextSeg);

                    double tPrevFt = GetWallThicknessFt(prevElem);
                    double tNextFt = GetWallThicknessFt(nextElem);

                    double calcLengthFt = GetSpaceCroppedWallLengthFt(seg, loop, segIndex, tPrevFt, tNextFt);
                    lengthMm = calcLengthFt * ft2mm;

                    // Высота стены
                    Parameter hParam = wall != null ? wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM) : null;
                    heightMm = (hParam != null && hParam.HasValue && hParam.AsDouble() > 0)
                        ? hParam.AsDouble() * ft2mm
                        : roomHeightFt * ft2mm;

                    areaSqM = (lengthMm / 1000.0) * (heightMm / 1000.0);
                }
                else if (cat == BuiltInCategory.OST_Floors ||
                         cat == BuiltInCategory.OST_Ceilings ||
                         cat == BuiltInCategory.OST_StructuralFoundation)
                {
                    Parameter areaParam = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    areaSqM = areaParam != null ? areaParam.AsDouble() * ft2m * ft2m : 0;

                    // Длина/высота — из BBox
                    BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                    if (bb != null)
                    {
                        lengthMm = Math.Abs(bb.Max.X - bb.Min.X) * ft2mm;
                        heightMm = Math.Abs(bb.Max.Y - bb.Min.Y) * ft2mm;
                    }
                }
                else // Двери, Окна
                {
                    Parameter widthParam  = elem.get_Parameter(BuiltInParameter.DOOR_WIDTH)
                                         ?? elem.get_Parameter(BuiltInParameter.CASEWORK_WIDTH)
                                         ?? elem.get_Parameter(BuiltInParameter.WINDOW_WIDTH);
                    Parameter heightParam = elem.get_Parameter(BuiltInParameter.DOOR_HEIGHT)
                                         ?? elem.get_Parameter(BuiltInParameter.CASEWORK_HEIGHT)
                                         ?? elem.get_Parameter(BuiltInParameter.WINDOW_HEIGHT);

                    lengthMm = widthParam  != null ? widthParam.AsDouble()  * ft2mm : 0;
                    heightMm = heightParam != null ? heightParam.AsDouble() * ft2mm : 0;

                    if (lengthMm == 0 || heightMm == 0)
                    {
                        BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                        if (bb != null)
                        {
                            lengthMm = Math.Abs(bb.Max.X - bb.Min.X) * ft2mm;
                            heightMm = Math.Abs(bb.Max.Z - bb.Min.Z) * ft2mm;
                        }
                    }

                    areaSqM = (lengthMm / 1000.0) * (heightMm / 1000.0);
                }
            }
            catch { /* Оставляем нули */ }
        }

        private static Dictionary<string, int> BuildWallSpaceCountMap(List<Space> spaces, SpatialElementBoundaryOptions boundaryOpts)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();
            if (spaces == null) return map;

            foreach (Space space in spaces)
            {
                IList<IList<BoundarySegment>> boundaries = space.GetBoundarySegments(boundaryOpts);
                if (boundaries == null) continue;

                HashSet<string> spaceWallKeys = new HashSet<string>();

                foreach (IList<BoundarySegment> loop in boundaries)
                {
                    if (loop == null) continue;
                    foreach (BoundarySegment seg in loop)
                    {
                        if (seg == null || seg.ElementId == ElementId.InvalidElementId) continue;

                        string key = (seg.LinkElementId != null && seg.LinkElementId != ElementId.InvalidElementId)
                            ? seg.ElementId.IntegerValue.ToString() + "_" + seg.LinkElementId.IntegerValue.ToString()
                            : seg.ElementId.IntegerValue.ToString();

                        spaceWallKeys.Add(key);
                    }
                }

                foreach (string key in spaceWallKeys)
                {
                    if (!map.ContainsKey(key))
                        map[key] = 0;
                    map[key]++;
                }
            }
            return map;
        }

        private static bool IsInteriorWall(Element boundElem, string wallKey, Dictionary<string, int> wallSpaceCountMap)
        {
            if (boundElem is Wall w)
            {
                // 1. Проверка свойства WallType.Function
                if (w.WallType != null && w.WallType.Function == WallFunction.Interior)
                    return true;

                // 2. Проверка наименования типа стены
                string typeName = w.WallType != null ? w.WallType.Name.ToLower() : "";
                string familyName = w.WallType != null ? w.WallType.FamilyName.ToLower() : "";
                string combinedName = typeName + " " + familyName;

                if (combinedName.Contains("внутр") ||
                    combinedName.Contains("перегород") ||
                    combinedName.Contains("межкомн") ||
                    combinedName.Contains("межквар") ||
                    combinedName.Contains("панель вн") ||
                    combinedName.Contains("int") ||
                    combinedName.Contains("part"))
                {
                    return true;
                }
            }

            // 3. Топологическая проверка (стена разделяет 2 или более помещений в модели)
            if (!string.IsNullOrEmpty(wallKey) && wallSpaceCountMap != null && wallSpaceCountMap.TryGetValue(wallKey, out int count))
            {
                if (count >= 2)
                    return true;
            }

            return false;
        }

        private static bool IsColinear(BoundarySegment s1, BoundarySegment s2)
        {
            if (s1 == null || s2 == null) return false;
            Curve c1 = s1.GetCurve();
            Curve c2 = s2.GetCurve();
            if (c1 == null || c2 == null) return false;

            XYZ p1_0 = c1.GetEndPoint(0);
            XYZ p1_1 = c1.GetEndPoint(1);
            XYZ p2_0 = c2.GetEndPoint(0);
            XYZ p2_1 = c2.GetEndPoint(1);

            XYZ v1 = (p1_1 - p1_0);
            XYZ v2 = (p2_1 - p2_0);
            if (v1.GetLength() < 0.001 || v2.GetLength() < 0.001) return false;

            XYZ u1 = v1.Normalize();
            XYZ u2 = v2.Normalize();

            XYZ cross = u1.CrossProduct(u2);
            return cross.GetLength() < 0.1;
        }

        private Transform GetSegmentTransform(BoundarySegment seg)
        {
            if (seg == null) return Transform.Identity;
            ElementId elemId = seg.ElementId;
            if (elemId == ElementId.InvalidElementId) return Transform.Identity;

            Element hostElem = _doc.GetElement(elemId);
            if (hostElem is RevitLinkInstance rvtLink)
            {
                return rvtLink.GetTotalTransform();
            }
            return Transform.Identity;
        }

        private double GetIntersectionParam2D(XYZ p0, double ux, double uy, BoundarySegment cornerSeg)
        {
            if (cornerSeg == null) return 0.0;
            Transform trans = GetSegmentTransform(cornerSeg);
            Curve curve = cornerSeg.GetCurve();
            if (curve == null) return 0.0;

            XYZ q0 = trans.OfPoint(curve.GetEndPoint(0));
            XYZ q1 = trans.OfPoint(curve.GetEndPoint(1));

            double vx = q1.X - q0.X;
            double vy = q1.Y - q0.Y;

            double det = ux * vy - uy * vx;
            if (Math.Abs(det) > 0.001)
            {
                double num = (q0.X - p0.X) * vy - (q0.Y - p0.Y) * vx;
                return num / det;
            }

            // Fallback если детерминант близко к 0 (параллельные линии)
            double t0 = (q0.X - p0.X) * ux + (q0.Y - p0.Y) * uy;
            double t1 = (q1.X - p0.X) * ux + (q1.Y - p0.Y) * uy;
            double d0 = (q0 - (p0 + new XYZ(ux, uy, 0) * t0)).GetLength();
            double d1 = (q1 - (p0 + new XYZ(ux, uy, 0) * t1)).GetLength();
            return (d0 < d1) ? t0 : t1;
        }

        private double GetSpaceCroppedWallLengthFt(
            BoundarySegment seg,
            IList<BoundarySegment> loop,
            int segIndex,
            double tPrevFt,
            double tNextFt)
        {
            Curve c = seg.GetCurve();
            if (c == null) return 0.0;

            Transform segTransform = GetSegmentTransform(seg);
            XYZ p0 = segTransform.OfPoint(c.GetEndPoint(0));
            XYZ p1 = segTransform.OfPoint(c.GetEndPoint(1));

            XYZ dir = (p1 - p0);
            double dirLen = dir.GetLength();

            if (dirLen < 0.001) return c.Length;

            double ux = dir.X / dirLen;
            double uy = dir.Y / dirLen;

            double spaceInnerLengthFt = c.Length;

            if (loop != null && loop.Count >= 3)
            {
                int loopCount = loop.Count;

                // Находим предыдущий не-коллинеарный сегмент контура
                BoundarySegment prevCornerSeg = null;
                for (int k = 1; k < loopCount; k++)
                {
                    BoundarySegment candidate = loop[(segIndex - k + loopCount) % loopCount];
                    if (candidate != null && !IsColinear(seg, candidate))
                    {
                        prevCornerSeg = candidate;
                        break;
                    }
                }

                // Находим следующий не-коллинеарный сегмент контура
                BoundarySegment nextCornerSeg = null;
                for (int k = 1; k < loopCount; k++)
                {
                    BoundarySegment candidate = loop[(segIndex + k) % loopCount];
                    if (candidate != null && !IsColinear(seg, candidate))
                    {
                        nextCornerSeg = candidate;
                        break;
                    }
                }

                if (prevCornerSeg != null && nextCornerSeg != null)
                {
                    double tCorner1 = GetIntersectionParam2D(p0, ux, uy, prevCornerSeg);
                    double tCorner2 = GetIntersectionParam2D(p0, ux, uy, nextCornerSeg);

                    double span = Math.Abs(tCorner2 - tCorner1);
                    if (span > 0.01 && span < dirLen + 10.0)
                    {
                        spaceInnerLengthFt = span;
                    }
                }
            }

            // Припуск до осей смежных стен
            double extraLengthFt = (tPrevFt / 2.0) + (tNextFt / 2.0);
            double finalLengthFt = spaceInnerLengthFt + extraLengthFt;

            return finalLengthFt;
        }

        private Element GetElementFromSegment(BoundarySegment seg)
        {
            if (seg == null) return null;
            ElementId elemId = seg.ElementId;
            if (elemId == ElementId.InvalidElementId) return null;

            Element hostElem = _doc.GetElement(elemId);
            if (hostElem is RevitLinkInstance rvtLink)
            {
                Document linkedDoc = rvtLink.GetLinkDocument();
                if (linkedDoc != null)
                {
                    try
                    {
                        ElementId linkedElemId = seg.LinkElementId;
                        if (linkedElemId != null && linkedElemId != ElementId.InvalidElementId)
                        {
                            return linkedDoc.GetElement(linkedElemId);
                        }
                    }
                    catch { }
                }
            }
            return hostElem;
        }

        private static double GetWallThicknessFt(Element elem)
        {
            if (elem is Wall wall)
            {
                try
                {
                    return wall.Width; // Толщина стены в футах
                }
                catch { }
            }
            return 0.0;
        }

        /// <summary>
        /// Ориентация конструкции по сторонам света (для стен) или горизонтальная.
        /// </summary>
        private string GetOrientation(Element elem, BuiltInCategory cat, BoundarySegment seg)
        {
            if (cat == BuiltInCategory.OST_Floors ||
                cat == BuiltInCategory.OST_Ceilings ||
                cat == BuiltInCategory.OST_StructuralFoundation)
                return "Горизонтальная";

            try
            {
                // Нормаль стены или направление сегмента
                XYZ normal = XYZ.BasisX;

                if (elem is Wall wall)
                {
                    normal = wall.Orientation;
                }
                else
                {
                    Curve c = seg.GetCurve();
                    XYZ dir = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
                    // Нормаль к сегменту (перпендикуляр в плане)
                    normal = new XYZ(-dir.Y, dir.X, 0);
                }

                return CompassFromVector(normal);
            }
            catch
            {
                return "";
            }
        }

        private static string CompassFromVector(XYZ v)
        {
            // Проецируем на XY и определяем сторону света
            double angle = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;

            // Восемь секторов
            if (angle < 22.5  || angle >= 337.5) return "В";   // Восток
            if (angle < 67.5)                    return "СВ";
            if (angle < 112.5)                   return "С";    // Север
            if (angle < 157.5)                   return "СЗ";
            if (angle < 202.5)                   return "З";    // Запад
            if (angle < 247.5)                   return "ЮЗ";
            if (angle < 292.5)                   return "Ю";    // Юг
            return "ЮВ";
        }

        /// <summary>
        /// Метка конструкции с использованием сокращений: НС1, НС2, ВС1, ДВ1, ОК1, ПР1, ПОТ1, КР1...
        /// </summary>
        private string GetConstructionLabel(Element elem, BuiltInCategory cat)
        {
            if (elem == null) return "НС1";

            ElementId typeId = elem.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId) typeId = elem.Id;

            if (_typeCodeMap != null && _typeCodeMap.TryGetValue(typeId, out string existingCode))
            {
                return existingCode;
            }

            string prefix = "НС"; // По умолчанию для ограждений — Наружная Стена (НС)

            Category category = elem.Category;
            BuiltInCategory bCat = cat;
            if (category != null)
            {
                try { bCat = category.BuiltInCategory; } catch { }
            }

            string catName = category?.Name ?? "";

            // 1. Стены
            if (elem is Wall || bCat == BuiltInCategory.OST_Walls || catName.IndexOf("Стен", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isInterior = false;
                if (elem is Wall wall)
                {
                    try
                    {
                        if (wall.WallType != null && wall.WallType.Function == WallFunction.Interior)
                            isInterior = true;
                    }
                    catch { }
                }

                prefix = isInterior ? "ВС" : "НС";
            }
            // 2. Окна
            else if (bCat == BuiltInCategory.OST_Windows || catName.IndexOf("Окн", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "ОК";
            }
            // 3. Двери
            else if (bCat == BuiltInCategory.OST_Doors || catName.IndexOf("Двер", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "ДВ";
            }
            // 4. Перекрытия и полы
            else if (elem is Floor || bCat == BuiltInCategory.OST_Floors || bCat == BuiltInCategory.OST_StructuralFoundation ||
                     catName.IndexOf("Перекрыт", StringComparison.OrdinalIgnoreCase) >= 0 || catName.IndexOf("Фундамент", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     catName.IndexOf("Пол", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "ПР";
            }
            // 5. Потолки
            else if (bCat == BuiltInCategory.OST_Ceilings || catName.IndexOf("Потол", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "ПОТ";
            }
            // 6. Кровля / Крыши
            else if (elem is RoofBase || bCat == BuiltInCategory.OST_Roofs ||
                     catName.IndexOf("Кровл", StringComparison.OrdinalIgnoreCase) >= 0 || catName.IndexOf("Крыш", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "КР";
            }

            if (_prefixCounterMap == null) _prefixCounterMap = new Dictionary<string, int>();
            if (_typeCodeMap == null) _typeCodeMap = new Dictionary<ElementId, string>();

            if (!_prefixCounterMap.ContainsKey(prefix))
            {
                _prefixCounterMap[prefix] = 1;
            }
            else
            {
                _prefixCounterMap[prefix]++;
            }

            string code = $"{prefix}{_prefixCounterMap[prefix]}";
            _typeCodeMap[typeId] = code;
            return code;
        }

        // ───────────────────────────────────────────────────────────────────
        // Вспомогательные: параметры
        // ───────────────────────────────────────────────────────────────────

        private static void SetText(Element elem, string paramName, string value)
        {
            Parameter p = elem.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                p.Set(value ?? "");
        }

        private static string GetText(Element elem, string paramName)
        {
            Parameter p = elem.LookupParameter(paramName);
            return p?.AsString() ?? "";
        }

        private static void SetNumber(Element elem, string paramName, double value)
        {
            Parameter p = elem.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return;

            if (p.StorageType == StorageType.Double)
                p.Set(value);
            else if (p.StorageType == StorageType.Integer)
                p.Set((int)Math.Round(value));
            else if (p.StorageType == StorageType.String)
                p.Set(value.ToString("G"));
        }

        private static BuiltInCategory GetBuiltInCategory(Element elem)
        {
            if (elem?.Category == null) return BuiltInCategory.INVALID;
            try
            {
                return elem.Category.BuiltInCategory;
            }
            catch
            {
#pragma warning disable CS0618
                return (BuiltInCategory)elem.Category.Id.IntegerValue;
#pragma warning restore CS0618
            }
        }
    }
}
