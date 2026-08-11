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

        // ───────────────────────────────────────────────────────────────────
        // Главный метод
        // ───────────────────────────────────────────────────────────────────
        public int Run(
            List<Space> spaces,
            FamilySymbol symbol,
            double tempOutside,
            double tempInside,
            bool processWalls,
            bool processFloors,
            bool processDoors,
            bool processWindows)
        {
            int placedCount = 0;

            using (Transaction tx = new Transaction(_doc, "BIMBCC Теплопотери — добавление параметров"))
            {
                tx.Start();
                EnsureProjectParameters();
                tx.Commit();
            }

            using (Transaction tx = new Transaction(_doc, "BIMBCC Теплопотери — расстановка кубиков"))
            {
                tx.Start();

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

                foreach (Space space in spaces)
                {
                    string roomNumber, roomName;
                    GetSpaceRoomNumberAndName(space, out roomNumber, out roomName);
                    double roomHeight = space.UnboundedHeight; // футы

                    IList<IList<BoundarySegment>> boundaries =
                        space.GetBoundarySegments(boundaryOpts);

                    // Собрать уникальные элементы-ограждения
                    HashSet<ElementId> processedIds = new HashSet<ElementId>();

                    foreach (IList<BoundarySegment> loop in boundaries)
                    {
                        foreach (BoundarySegment seg in loop)
                        {
                            ElementId boundElemId = seg.ElementId;
                            if (boundElemId == ElementId.InvalidElementId) continue;
                            if (!processedIds.Add(boundElemId)) continue;

                            Element boundElem = _doc.GetElement(boundElemId);
                            if (boundElem == null) continue;

                            // Определить тип конструкции
                            BuiltInCategory cat = GetBuiltInCategory(boundElem);

                            bool isWall    = (cat == BuiltInCategory.OST_Walls);
                            bool isFloor   = (cat == BuiltInCategory.OST_Floors  ||
                                              cat == BuiltInCategory.OST_Ceilings ||
                                              cat == BuiltInCategory.OST_StructuralFoundation);
                            bool isDoor    = (cat == BuiltInCategory.OST_Doors);
                            bool isWindow  = (cat == BuiltInCategory.OST_Windows);

                            if (isWall   && !processWalls)   continue;
                            if (isFloor  && !processFloors)  continue;
                            if (isDoor   && !processDoors)   continue;
                            if (isWindow && !processWindows) continue;

                            if (!isWall && !isFloor && !isDoor && !isWindow) continue;

                            // Вычислить точку размещения
                            XYZ placementPoint = GetPlacementPoint(boundElem, space, seg, roomHeight);
                            if (placementPoint == null) continue;

                            // Разместить экземпляр
                            FamilyInstance inst = _doc.Create.NewFamilyInstance(
                                placementPoint,
                                symbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (inst == null) continue;

                            // Вычислить геометрические характеристики
                            double lengthMm  = 0;
                            double heightMm  = 0;
                            double areaSqM   = 0;
                            string label     = GetConstructionLabel(boundElem, cat);
                            string orient    = GetOrientation(boundElem, cat, seg);

                            GetConstructionDimensions(boundElem, cat, seg, roomHeight,
                                out lengthMm, out heightMm, out areaSqM);

                            // Заполнить параметры
                            SetText(inst, P_ROOM_NUMBER,  roomNumber);
                            SetText(inst, P_ROOM_NAME,    roomName);
                            SetText(inst, P_CONSTR_LABEL, label);
                            SetText(inst, P_ORIENTATION,  orient);
                            SetText(inst, P_CORNER_TYPE,  "");

                            SetNumber(inst, P_TEMP_OUT,  tempOutside);
                            SetNumber(inst, P_TEMP_IN,   tempInside);
                            SetNumber(inst, P_LENGTH,    lengthMm);
                            SetNumber(inst, P_HEIGHT,    heightMm);
                            SetNumber(inst, P_AREA,      areaSqM);
                            // Расчётные коэффициенты — пустые (заполняются пользователем)
                            SetNumber(inst, P_COEFF_N,   0);
                            SetNumber(inst, P_COEFF_K,   0);
                            SetNumber(inst, P_ADD_B1,    0);
                            SetNumber(inst, P_ADD_B2,    0);
                            SetNumber(inst, P_ADD_B3,    0);
                            SetNumber(inst, P_ADD_B4,    0);
                            SetNumber(inst, P_COEFF_ADD, 0);
                            SetNumber(inst, P_HEAT_LOSS, 0);

                            placedCount++;
                        }
                    }
                }

                tx.Commit();
            }

            return placedCount;
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
                    def.IsItemized = true;

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

                    // Собрать доступные поля (без ToDictionary во избежание выброса ошибок на дубликаты имён)
                    IList<SchedulableField> schedulable = def.GetSchedulableFields();

                    foreach (string paramName in fieldOrder)
                    {
                        SchedulableField sf = schedulable.FirstOrDefault(f => f.GetName(_doc) == paramName);
                        if (sf != null)
                        {
                            def.AddField(sf);
                        }
                    }

                    // Сортировка: Номер → Имя помещения
                    ScheduleSortGroupField sortByNumber = null;
                    ScheduleSortGroupField sortByName   = null;

                    foreach (ScheduleField addedField in def.GetFieldOrder()
                        .Select(id => def.GetField(id)))
                    {
                        string fn = addedField.GetSchedulableField().GetName(_doc);
                        if (fn == P_ROOM_NUMBER && sortByNumber == null)
                            sortByNumber = new ScheduleSortGroupField(addedField.FieldId);
                        if (fn == P_ROOM_NAME && sortByName == null)
                            sortByName = new ScheduleSortGroupField(addedField.FieldId);
                    }

                    if (sortByNumber != null) def.AddSortGroupField(sortByNumber);
                    if (sortByName   != null) def.AddSortGroupField(sortByName);

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
        /// Точка размещения кубика — центр грани ограждения.
        /// </summary>
        private XYZ GetPlacementPoint(
            Element elem, Space space, BoundarySegment seg, double roomHeightFt)
        {
            try
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                if (bb == null)
                {
                    // Fallback: середина сегмента на половине высоты пространства
                    Curve c = seg.GetCurve();
                    XYZ mid = c.Evaluate(0.5, true);
                    double spaceZ = space.Level != null
                        ? space.Level.ProjectElevation
                        : mid.Z;
                    return new XYZ(mid.X, mid.Y, spaceZ + roomHeightFt / 2.0);
                }

                double cx = (bb.Min.X + bb.Max.X) / 2.0;
                double cy = (bb.Min.Y + bb.Max.Y) / 2.0;
                double cz = (bb.Min.Z + bb.Max.Z) / 2.0;
                return new XYZ(cx, cy, cz);
            }
            catch
            {
                return null;
            }
        }

        private void GetConstructionDimensions(
            Element elem,
            BuiltInCategory cat,
            BoundarySegment seg,
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
                    if (wall != null)
                    {
                        // Длина по сегменту
                        Curve c = seg.GetCurve();
                        lengthMm = c.Length * ft2mm;

                        // Высота стены
                        Parameter hParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                        heightMm = hParam != null
                            ? hParam.AsDouble() * ft2mm
                            : roomHeightFt * ft2mm;

                        areaSqM = (lengthMm / 1000.0) * (heightMm / 1000.0);
                    }
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
        /// Метка конструкции: тип элемента + марка типа.
        /// </summary>
        private string GetConstructionLabel(Element elem, BuiltInCategory cat)
        {
            string typeName = "";
            ElementType eType = _doc.GetElement(elem.GetTypeId()) as ElementType;
            if (eType != null) typeName = eType.Name;

            switch (cat)
            {
                case BuiltInCategory.OST_Walls:                  return $"Стена: {typeName}";
                case BuiltInCategory.OST_Floors:                 return $"Перекрытие: {typeName}";
                case BuiltInCategory.OST_Ceilings:               return $"Потолок: {typeName}";
                case BuiltInCategory.OST_StructuralFoundation:   return $"Плита: {typeName}";
                case BuiltInCategory.OST_Doors:                  return $"Дверь: {typeName}";
                case BuiltInCategory.OST_Windows:                return $"Окно: {typeName}";
                default:                                          return typeName;
            }
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
#pragma warning disable CS0618
            return (BuiltInCategory)elem.Category.Id.IntegerValue;
#pragma warning restore CS0618
        }
    }
}
