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
                // Collect space element IDs from current UI selection
                ICollection<ElementId> currentSelection = uidoc.Selection.GetElementIds();
                List<ElementId> selectedSpaceIds = currentSelection
                    .Select(id => doc.GetElement(id))
                    .Where(e => e is Space)
                    .Select(e => e.Id)
                    .ToList();

                HeatLossWindow window = new HeatLossWindow(doc, selectedSpaceIds);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    HeatLossEngine engine = new HeatLossEngine(doc);

                    var targetSpaces = engine.GetTargetSpaces(
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

                    HeatLossCalculationResult calculationResult = null;

                    using (Transaction trans = new Transaction(doc, "BIMBCC Теплопотери - Расстановка маркеров"))
                    {
                        trans.Start();

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
                            (msg, pct) => progress.UpdateProgress(msg, pct)
                        );

                        trans.Commit();
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
