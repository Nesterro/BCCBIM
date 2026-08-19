using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class FilterSelectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            try
            {
                FilterSelectionWindow win = new FilterSelectionWindow(uidoc.Document, uidoc.ActiveView);
                if (win.ShowDialog() == true && win.MatchingElementIds != null)
                {
                    uidoc.Selection.SetElementIds(win.MatchingElementIds);
                    TaskDialog.Show("Выбор по фильтрам", $"Найдено и выделено {win.MatchingElementIds.Count} элементов.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
