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
                    window.ProcessExteriorWalls,
                    window.ProcessInteriorWalls,
                    window.ProcessFloors,
                    window.ProcessDoors,
                    window.ProcessWindows,
                    window.ExteriorOptions);

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

                // ── 8. Итоговое сообщение с предложением открыть в Revit и экспортировать в Excel ───
                ViewSchedule schedView = null;
                if (!string.IsNullOrEmpty(schedName))
                {
                    try
                    {
                        schedView = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSchedule))
                            .Cast<ViewSchedule>()
                            .FirstOrDefault(vs => vs.Name == schedName);
                    }
                    catch { }
                }

                TaskDialog td = new TaskDialog("BIMBCC | Теплопотери");
                td.MainInstruction = "Расстановка и расчёт теплопотерь завершены!";
                td.MainContent =
                    $"• Размещено кубиков: {placedCount}\n" +
                    $"• Пространств обработано: {spaces.Count}\n" +
                    $"• Рассчитано элементов (Q): {calculatedCount}\n" +
                    (schedName != null
                        ? $"• Спецификация: «{schedName}» готова."
                        : $"• Предупреждение по спецификации: {schedError}");

                if (schedView != null)
                {
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "📋 Открыть таблицу теплопотерь в Revit",
                        "Перейти к сформированной спецификации в текущем окне Revit");

                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "📊 Экспортировать в Excel (с сохранением формул)",
                        "Сохранить таблицу в файл .xlsx с рабочими формулами расчёта надбавок и теплопотерь Q");

                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                        "🚀 Открыть в Revit и экспортировать в Excel",
                        "Открыть спецификацию в Revit и сразу сохранить файл Excel");
                }

                td.CommonButtons = TaskDialogCommonButtons.Close;
                td.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult res = td.Show();

                bool shouldOpenInRevit = (res == TaskDialogResult.CommandLink1 || res == TaskDialogResult.CommandLink3);
                bool shouldExportExcel = (res == TaskDialogResult.CommandLink2 || res == TaskDialogResult.CommandLink3);

                if (shouldOpenInRevit && schedView != null)
                {
                    try
                    {
                        uidoc.ActiveView = schedView;
                    }
                    catch { }
                }

                if (shouldExportExcel)
                {
                    PromptAndExportToExcel(doc);
                }

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

        private static void PromptAndExportToExcel(Document doc)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Сохранить расчёт теплопотерь в Excel",
                    Filter = "Книга Excel (*.xlsx)|*.xlsx",
                    FileName = $"BIMBCC_Теплопотери_{DateTime.Now:yyyy-MM-dd_HHmm}.xlsx",
                    DefaultExt = ".xlsx"
                };

                if (sfd.ShowDialog() == true)
                {
                    string exportErr;
                    bool ok = HeatLossExcelExporter.ExportToExcel(doc, sfd.FileName, out exportErr);

                    if (ok)
                    {
                        var askOpen = MessageBox.Show(
                            $"Файл успешно сохранен:\n{sfd.FileName}\n\nОткрыть файл в Excel сейчас?",
                            "Экспорт в Excel завершён",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (askOpen == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName)
                                {
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Ошибка при экспорте в Excel:\n{exportErr}",
                            "Ошибка экспорта",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось выполнить экспорт в Excel:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
