using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SchedulePackCommand : IExternalCommand
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
                // Find all project schedules
                List<ViewSchedule> schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTitleblockRevisionSchedule && !s.IsTemplate)
                    .OrderBy(s => s.Name)
                    .ToList();

                if (schedules.Count == 0)
                {
                    MessageBox.Show("В текущем проекте не найдено спецификаций.", "Экспорт спецификаций", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                string defaultFolder = GetDefaultExportDirectory(doc);

                SchedulePackWindow window = new SchedulePackWindow(schedules, defaultFolder);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    var selectedSchedules = window.SelectedSchedules;
                    string folderPath = window.SelectedFolderPath;
                    bool isCsv = window.ExportAsCsv;

                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    // Open Progress window
                    ProgressWindow progress = new ProgressWindow();
                    WindowInteropHelper progressHelper = new WindowInteropHelper(progress);
                    progressHelper.Owner = uiapp.MainWindowHandle;
                    progress.Show();

                    int total = selectedSchedules.Count;
                    int exportedCount = 0;

                    ViewScheduleExportOptions options = new ViewScheduleExportOptions
                    {
                        FieldDelimiter = isCsv ? ";" : "\t",
                        TextQualifier = ExportTextQualifier.None,
                        Title = true,
                        ColumnHeaders = ExportColumnHeaders.OneRow
                    };

                    for (int i = 0; i < total; i++)
                    {
                        ViewSchedule schedule = selectedSchedules[i];
                        double pct = ((double)(i + 1) / total) * 100;
                        progress.UpdateProgress($"Экспорт спецификации {i + 1} из {total}: {schedule.Name}...", pct);

                        string sanitizedFileName = SanitizeFileName(schedule.Name) + (isCsv ? ".csv" : ".txt");

                        try
                        {
                            schedule.Export(folderPath, sanitizedFileName, options);
                            exportedCount++;
                        }
                        catch { }
                    }

                    progress.Close();

                    var openResult = MessageBox.Show(
                        $"Успешно экспортировано {exportedCount} из {total} спецификаций!\n\nОткрыть папку с файлами?",
                        "BIMBCC | Экспорт спецификаций",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (openResult == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }

                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при экспорте спецификаций:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }

        private string SanitizeFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c.ToString(), "");
            }
            return name.Trim();
        }

        private string GetDefaultExportDirectory(Document doc)
        {
            try
            {
                if (!doc.IsModelInCloud && !string.IsNullOrEmpty(doc.PathName))
                {
                    string dir = Path.GetDirectoryName(doc.PathName);
                    if (Directory.Exists(dir)) return dir;
                }
            }
            catch { }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
