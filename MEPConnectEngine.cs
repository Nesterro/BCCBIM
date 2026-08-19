using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class MEPConnectEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public MEPConnectEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void ConnectMEPElements()
        {
            Reference ref1 = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите первый элемент MEP для соединения");
            Reference ref2 = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите второй элемент MEP для соединения");

            Element e1 = _doc.GetElement(ref1.ElementId);
            Element e2 = _doc.GetElement(ref2.ElementId);

            Connector c1 = GetUnconnectedConnector(e1);
            Connector c2 = GetUnconnectedConnector(e2);

            if (c1 == null || c2 == null) throw new Exception("Не найдены свободные коннекторы на элементах.");

            using (Transaction t = new Transaction(_doc, "BIMBCC: MEP соединение"))
            {
                t.Start();
                try
                {
                    c1.ConnectTo(c2);
                }
                catch
                {
                    _doc.Create.NewElbowFitting(c1, c2);
                }
                t.Commit();
            }
        }

        private Connector GetUnconnectedConnector(Element elem)
        {
            ConnectorSet connectors = null;
            if (elem is MEPCurve curve) connectors = curve.ConnectorManager.Connectors;
            else if (elem is FamilyInstance fi && fi.MEPModel != null) connectors = fi.MEPModel.ConnectorManager.Connectors;

            if (connectors == null) return null;

            foreach (Connector c in connectors)
            {
                if (!c.IsConnected) return c;
            }
            return connectors.Cast<Connector>().FirstOrDefault();
        }
    }
}
