using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class WallElevationsCommand : IExternalCommand
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
                WallElevationsWindow win = new WallElevationsWindow();
                if (win.ShowDialog() == true)
                {
                    WallElevationsEngine engine = new WallElevationsEngine(uidoc.Document);
                    int count = engine.CreateInteriorElevations(selectedIds, win.CreateNorth, win.CreateEast, win.CreateSouth, win.CreateWest);
                    TaskDialog.Show("Развертки стен", $"Успешно создано {count} видов разверток стен.");
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
