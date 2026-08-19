using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class RoomFloorsCommand : IExternalCommand
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
                RoomFloorsWindow win = new RoomFloorsWindow(uidoc.Document);
                if (win.ShowDialog() == true)
                {
                    RoomFloorsEngine engine = new RoomFloorsEngine(uidoc.Document);
                    int count = engine.CreateRoomFloors(selectedIds, win.SelectedFloorType);
                    TaskDialog.Show("Пол по помещению", $"Успешно создано {count} перекрытий чистых полов.");
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
