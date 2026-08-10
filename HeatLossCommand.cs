using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HeatLossCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                HeatLossEngine engine = new HeatLossEngine(doc);

                // Collect spaces, links, and cube symbols
                var linkInstances = engine.GetRevitLinkInstances();
                var cubeSymbols = engine.GetAvailableCubeSymbols();

                List<ElementId> selectedSpaceIds = new List<ElementId>();
                try
                {
                    Selection selection = uidoc.Selection;
                    ICollection<ElementId> selectedIds = selection.GetElementIds();
                    foreach (ElementId id in selectedIds)
                    {
                        Element elem = doc.GetElement(id);
                        if (elem is Space)
                        {
                            selectedSpaceIds.Add(id);
                        }
                    }
                }
                catch { }

                HeatLossWindow window = new HeatLossWindow(doc, selectedSpaceIds);

                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    List<Space> targetSpaces = engine.GetTargetSpaces(
                        window.ScopeMode,
                        selectedSpaceIds,
                        window.SelectedLevelId);

                    if (targetSpaces.Count == 0)
                    {
                        MessageBox.Show("В проекте не найдено пространств по выбранному критерию.",
                            "BIMBCC | Теплопотери", MessageBoxButton.OK, MessageBoxImage.Information);
                        return Result.Cancelled;
                    }

                    // Open Progress window
                    ProgressWindow progress = new ProgressWindow();
                    WindowInteropHelper progressHelper = new WindowInteropHelper(progress);
                    progressHelper.Owner = uiapp.MainWindowHandle;
                    progress.SetHeaderTitle(" | ВЫПОЛНЕНИЕ РАСЧЁТА ТЕПЛОПОТЕРЬ");
                    progress.Show();

                    HeatLossCalculationResult calculationResult = new HeatLossCalculationResult();

                    // PREPARATION: Load/Create shared parameter definitions OUTSIDE ANY TRANSACTION
                    progress.UpdateProgress("Подготовка определений параметров...", 5.0);
                    var definitions = engine.GetOrCreateHeatLossDefinitions(calculationResult);

                    // =========================================================================
                    // ИТЕРАЦИЯ 1: СОЗДАНИЕ И ПРИВЯЗКА ПАРАМЕТРОВ К ПРОЕКТУ (ТРАНЗАКЦИЯ 1)
                    // =========================================================================
                    progress.UpdateProgress("Итерация 1 из 3: Создание и привязка проектных параметров...", 15.0);
                    using (Transaction trans1 = new Transaction(doc, "BIMBCC Теплопотери - Итерация 1: Параметры"))
                    {
                        trans1.Start();
                        engine.BindHeatLossProjectParameters(definitions, calculationResult);
                        trans1.Commit();
                    }

                    // =========================================================================
                    // ИТЕРАЦИЯ 2: АНАЛИЗ ГЕОМЕТРИИ И РАССТАНОВКА МАРКЕРОВ (ТРАНЗАКЦИЯ 2)
                    // =========================================================================
                    progress.UpdateProgress("Итерация 2 из 3: Расстановка маркеров по пространствам...", 35.0);
                    List<PlacedCubeInfo> placedCubes = null;
                    using (Transaction trans2 = new Transaction(doc, "BIMBCC Теплопотери - Итерация 2: Расстановка маркеров"))
                    {
                        trans2.Start();
                        placedCubes = engine.PlaceCubeMarkers(
                            targetSpaces,
                            window.SelectedCubeSymbol,
                            window.SelectedLinkInstance,
                            window.LinkedParamName,
                            window.OutdoorTemp,
                            window.DeleteExistingCubes,
                            calculationResult,
                            (msg, pct) => progress.UpdateProgress(msg, 35.0 + (pct * 0.35))
                        );
                        trans2.Commit();
                    }

                    // =========================================================================
                    // ИТЕРАЦИЯ 3: ПЕРЕЗАПИСЬ ПАРАМЕТРОВ И ФОРМИРОВАНИЕ СПЕЦИФИКАЦИИ (ТРАНЗАКЦИЯ 3)
                    // =========================================================================
                    progress.UpdateProgress("Итерация 3 из 3: Запись параметров в кубики и формирование спецификации...", 70.0);
                    using (Transaction trans3 = new Transaction(doc, "BIMBCC Теплопотери - Итерация 3: Перезапись параметров и спецификация"))
                    {
                        trans3.Start();
                        engine.WriteParametersToCubesAndCreateSchedule(
                            placedCubes,
                            window.TargetDesignationParamName,
                            window.TargetAreaParamName,
                            window.CreateSchedule,
                            window.ExportCsv,
                            window.CsvExportPath,
                            calculationResult,
                            (msg, pct) => progress.UpdateProgress(msg, 70.0 + (pct * 0.30))
                        );
                        trans3.Commit();
                    }

                    progress.Close();

                    string summaryLog = string.Join("\n", calculationResult.Logs);
                    MessageBox.Show(
                        $"Обработка завершена!\n\n" +
                        $"Пространств обработано: {calculationResult.SpacesProcessedCount}\n" +
                        $"Кубиков расставлено: {calculationResult.CubesPlacedCount}\n" +
                        (calculationResult.DeletedCubesCount > 0 ? $"Старых кубиков удалено: {calculationResult.DeletedCubesCount}\n\n" : "\n") +
                        $"{summaryLog}",
                        "BIMBCC | Теплопотери",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при выполнении модуля «Теплопотери»:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}
