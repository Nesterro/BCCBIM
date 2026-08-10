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

                    // PHASE 1: Create and bind project parameters in a dedicated transaction (COMMITTED BEFORE CUBE PLACEMENT)
                    progress.UpdateProgress("Этап 1: Добавление проектных параметров в файл Revit...", 10.0);
                    using (Transaction trans1 = new Transaction(doc, "BIMBCC Теплопотери - Создание проектных параметров"))
                    {
                        trans1.Start();
                        engine.EnsureHeatLossProjectParametersExist(calculationResult);
                        trans1.Commit();
                    }

                    // PHASE 2: Geometry analysis, cube placement, parameter writing, schedule generation
                    progress.UpdateProgress("Этап 2: Расстановка маркеров и формирование спецификации...", 20.0);
                    using (Transaction trans2 = new Transaction(doc, "BIMBCC Теплопотери - Расстановка маркеров и спецификация"))
                    {
                        trans2.Start();

                        calculationResult = engine.ProcessSpaces(
                            targetSpaces,
                            window.SelectedCubeSymbol,
                            window.SelectedLinkInstance,
                            window.LinkedParamName,
                            window.TargetDesignationParamName,
                            window.TargetAreaParamName,
                            window.OutdoorTemp,
                            window.DeleteExistingCubes,
                            window.CreateSchedule,
                            window.ExportCsv,
                            window.CsvExportPath,
                            calculationResult,
                            (msg, pct) => progress.UpdateProgress(msg, 20.0 + (pct * 0.8))
                        );

                        trans2.Commit();
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
