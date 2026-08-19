using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class CopyElementsCommand : IExternalCommand
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
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                CopyElementsWindow win = new CopyElementsWindow(uidoc.Document);
                if (win.ShowDialog() == true)
                {
                    CopyElementsEngine engine = new CopyElementsEngine(uidoc.Document);
                    int count = engine.CopyElementsToLevels(selectedIds, win.SelectedLevels);
                    TaskDialog.Show("Копировать элементы", $"Успешно создано {count} копий элементов.");
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
