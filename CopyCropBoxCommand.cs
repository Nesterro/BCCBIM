using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class CopyCropBoxCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            if (activeView == null || activeView.IsTemplate)
            {
                message = "Пожалуйста, откройте графический вид-источник (не шаблон).";
                return Result.Failed;
            }

            List<View> allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Id != activeView.Id && v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();

            if (allViews.Count == 0)
            {
                TaskDialog.Show("Внимание", "В проекте не найдено других подходящих целевых видов.");
                return Result.Succeeded;
            }

            CopyCropBoxEngine engine = new CopyCropBoxEngine(doc);
            int copiedCount = engine.CopyCropBoxToViews(activeView, allViews);

            TaskDialog.Show("Успешно", $"Границы обрезки и секущий диапазон успешно скопированы на {copiedCount} видов.");
            return Result.Succeeded;
        }
    }
}
