using System;
using System.Collections.Generic;
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
    public class LevelingCommand : IExternalCommand
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
            var selectedIds = uidoc.Selection.GetElementIds();

            try
            {
                LevelingWindow window = new LevelingWindow(doc, selectedIds.Count > 0);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    List<Element> targetElements = new List<Element>();

                    if (window.Scope == LevelingScope.SelectedElements)
                    {
                        targetElements = selectedIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                    }
                    else if (window.Scope == LevelingScope.Category && window.SelectedCategory != null)
                    {
                        targetElements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategoryId(window.SelectedCategory.Id)
                            .WhereElementIsNotElementType()
                            .ToList();
                    }
                    else
                    {
                        // Active view elements
                        targetElements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .WhereElementIsNotElementType()
                            .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                            .ToList();
                    }

                    if (targetElements.Count == 0)
                    {
                        MessageBox.Show("Не найдено элементов для привязки.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return Result.Succeeded;
                    }

                    // Get all levels sorted by elevation
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    if (levels.Count == 0)
                    {
                        MessageBox.Show("В проекте не найдено уровней.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        return Result.Failed;
                    }

                    // Open Progress window
                    ProgressWindow progress = new ProgressWindow();
                    WindowInteropHelper progressHelper = new WindowInteropHelper(progress);
                    progressHelper.Owner = uiapp.MainWindowHandle;
                    progress.Show();

                    int total = targetElements.Count;
                    int updatedCount = 0;

                    using (Transaction trans = new Transaction(doc, "BIMBCC Привязка к уровням"))
                    {
                        trans.Start();

                        for (int i = 0; i < total; i++)
                        {
                            Element elem = targetElements[i];
                            double pct = ((double)(i + 1) / total) * 100;
                            progress.UpdateProgress($"Обработка элемента {i + 1} из {total}: {elem.Name}...", pct);

                            XYZ pos = GetElementLocation(elem);
                            if (pos == null) continue;

                            // Find nearest level below or closest
                            Level targetLevel = FindNearestLevel(levels, pos.Z);
                            if (targetLevel == null) continue;

                            bool updated = AssignLevelToElement(elem, targetLevel, pos.Z, window.RecalculateOffset);
                            if (updated) updatedCount++;
                        }

                        trans.Commit();
                    }

                    progress.Close();

                    MessageBox.Show(
                        $"Успешно привязано {updatedCount} из {total} элементов к уровням!",
                        "BIMBCC | Привязка к уровням",
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
                MessageBox.Show($"Произошла ошибка при привязке к уровням:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }

        private XYZ GetElementLocation(Element elem)
        {
            if (elem.Location is LocationPoint lp) return lp.Point;
            if (elem.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);

            BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;

            return null;
        }

        private Level FindNearestLevel(List<Level> levels, double zElevation)
        {
            // Find highest level at or below zElevation
            Level levelBelow = levels.LastOrDefault(l => l.Elevation <= zElevation);
            if (levelBelow != null) return levelBelow;

            // Otherwise pick lowest available level
            return levels.FirstOrDefault();
        }

        private bool AssignLevelToElement(Element elem, Level targetLevel, double elemZ, bool recalculateOffset)
        {
            try
            {
                Parameter levelParam = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                     ?? elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);

                if (levelParam != null && !levelParam.IsReadOnly)
                {
                    levelParam.Set(targetLevel.Id);

                    if (recalculateOffset)
                    {
                        Parameter offsetParam = elem.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                        if (offsetParam != null && !offsetParam.IsReadOnly)
                        {
                            double newOffset = elemZ - targetLevel.Elevation;
                            offsetParam.Set(newOffset);
                        }
                    }

                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
