using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class TaglessCommand : IExternalCommand
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
                TaglessWindow win = new TaglessWindow(uidoc.Document, uidoc.ActiveView);
                if (win.ShowDialog() == true)
                {
                    TaglessEngine engine = new TaglessEngine(uidoc);
                    int count = engine.SelectUntaggedElementsOnActiveView(win.SelectedCategoryIds);
                    TaskDialog.Show("Элементы без марок", $"На текущем виде найдено {count} немаркированных элементов в выбранных категориях. Они успешно выделены в модели.");
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
