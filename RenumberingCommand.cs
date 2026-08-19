using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class RenumberingCommand : IExternalCommand
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
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    message = "Пожалуйста, выделите элементы для нумерации.";
                    return Result.Failed;
                }

                RenumberingWindow win = new RenumberingWindow();
                if (win.ShowDialog() == true)
                {
                    Document doc = uidoc.Document;
                    int currentNum = win.StartNum;
                    int count = 0;

                    using (Transaction t = new Transaction(doc, "BIMBCC: Нумерация элементов"))
                    {
                        t.Start();
                        foreach (ElementId id in selectedIds)
                        {
                            Element elem = doc.GetElement(id);
                            if (elem == null) continue;

                            Parameter pNum = elem.get_Parameter(BuiltInParameter.ROOM_NUMBER)
                                          ?? elem.get_Parameter(BuiltInParameter.DOOR_NUMBER)
                                          ?? elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)
                                          ?? elem.LookupParameter("Марка")
                                          ?? elem.LookupParameter("Номер");

                            if (pNum != null && !pNum.IsReadOnly)
                            {
                                try
                                {
                                    pNum.Set($"{win.Prefix}{currentNum}");
                                    currentNum++;
                                    count++;
                                }
                                catch { }
                            }
                        }
                        t.Commit();
                    }
                    TaskDialog.Show("Нумерация элементов", $"Пронумеровано {count} элементов.");
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
