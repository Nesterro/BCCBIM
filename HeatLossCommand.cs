using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HeatLossCommand : IExternalCommand
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
                // ── 1. Проверить наличие пространств ────────────────────────
                int allSpacesCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Space>()
                    .Count(s => s.Area > 0);

                int activeViewSpacesCount = 0;
                if (doc.ActiveView != null)
                {
                    activeViewSpacesCount = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Cast<Space>()
                        .Count(s => s.Area > 0);
                }

                if (allSpacesCount == 0)
                {
                    MessageBox.Show(
                        "В модели не найдено ни одного пространства (MEP Space) с площадью > 0.\n" +
                        "Убедитесь, что пространства расставлены и имеют объём.",
                        "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // ── 2. Собрать семейства «Обобщённые модели» ─────────────────
                List<FamilySymbol> genericSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Category != null &&
                                s.Category.Id == new ElementId(BuiltInCategory.OST_GenericModel))
                    .OrderBy(s => s.Family.Name)
                    .ThenBy(s => s.Name)
                    .ToList();

                if (genericSymbols.Count == 0)
                {
                    MessageBox.Show(
                        "В проекте не найдено семейств категории «Обобщённые модели».\n" +
                        "Загрузите семейство-кубик для расстановки теплопотерь.",
                        "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // ── 3. Показать диалог ────────────────────────────────────────
                HeatLossWindow window = new HeatLossWindow(genericSymbols, allSpacesCount, activeViewSpacesCount);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() != true)
                    return Result.Cancelled;

                // ── 4. Собрать пространства согласно выбору области ─────────
                List<Space> spaces;
                if (window.OnlyActiveView && doc.ActiveView != null)
                {
                    spaces = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Cast<Space>()
                        .Where(s => s.Area > 0)
                        .ToList();
                }
                else
                {
                    spaces = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Cast<Space>()
                        .Where(s => s.Area > 0)
                        .ToList();
                }

                if (spaces.Count == 0)
                {
                    MessageBox.Show(
                        "В выбранной области не найдено пространств для обработки.",
                        "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // ── 5. Запустить движок ───────────────────────────────────────
                // ── 5. Транзакция 1: Геометрический расчёт и расстановка кубиков ───
                HeatLossEngine engine = new HeatLossEngine(doc);

                int placedCount = engine.Run(
                    spaces,
                    window.SelectedSymbol,
                    window.TempOutside,
                    window.TempInside,
                    window.ProcessWalls,
                    window.ProcessFloors,
                    window.ProcessDoors,
                    window.ProcessWindows);

                // ── 6. Создать / обновить спецификацию ──────────────────────────────
                string schedError;
                string schedName = engine.CreateOrUpdateSchedule(out schedError);

                // ── 7. Диалог 2: Задание коэффициентов k и Транзакция 2 ──────────────
                List<HeatLossCoeffItem> coeffItems = engine.GetPlacedConstructionTypes();
                int calculatedCount = 0;

                if (coeffItems.Count > 0)
                {
                    HeatLossCoeffWindow coeffWindow = new HeatLossCoeffWindow(coeffItems);
                    WindowInteropHelper coeffHelper = new WindowInteropHelper(coeffWindow);
                    coeffHelper.Owner = uiapp.MainWindowHandle;

                    if (coeffWindow.ShowDialog() == true)
                    {
                        Dictionary<string, double> kMap = coeffWindow.Items
                            .ToDictionary(item => item.Code, item => item.CoeffK);

                        calculatedCount = engine.ApplyCoefficientsAndCalculateHeatLoss(kMap);
                    }
                }

                // ── 8. Итоговое сообщение ───────────────────────────────────────────
                TaskDialog td = new TaskDialog("BIMBCC | Теплопотери");
                td.MainInstruction = "Расстановка и расчёт теплопотерь завершены!";
                td.MainContent =
                    $"Размещено кубиков:  {placedCount}\n" +
                    $"Пространств обработано:  {spaces.Count}\n" +
                    $"Рассчитано элементов (Q):  {calculatedCount}\n" +
                    (schedName != null
                        ? $"Спецификация:  «{schedName}» создана/обновлена."
                        : $"Спецификацию создать не удалось: {schedError}");
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show(
                    $"Ошибка при расстановке теплопотерь:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}
