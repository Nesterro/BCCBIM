using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    /// <summary>
    /// Автономный генератор Excel (.xlsx) без внешних зависимостей.
    /// Формирует стандартный OpenXML Spreadsheet пакет с живыми формулами и форматированием.
    /// </summary>
    public static class HeatLossExcelExporter
    {
        public static bool ExportToExcel(Document doc, string filePath, out string errorMessage)
        {
            errorMessage = null;
            if (doc == null)
            {
                errorMessage = "Документ Revit не инициализирован.";
                return false;
            }

            try
            {
                // 1. Собрать все размещенные кубики теплопотерь
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                List<FamilyInstance> cubes = collector
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(e => {
                        // Проверяем наличие любого из характерных параметров теплопотерь
                        if (e.LookupParameter(HeatLossEngine.P_CONSTR_LABEL) != null) return true;
                        if (e.LookupParameter(HeatLossEngine.P_ROOM_NUMBER) != null) return true;
                        if (e.LookupParameter(HeatLossEngine.P_HEAT_LOSS) != null) return true;
                        if (e.LookupParameter("BIMBCC_SpaceNumber") != null) return true;
                        if (e.LookupParameter("BIMBCC_HeatLoss") != null) return true;

                        Parameter pMark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (pMark != null && pMark.HasValue)
                        {
                            string mark = pMark.AsString();
                            if (!string.IsNullOrEmpty(mark) && (mark == "BCC_HL_CUBE" || mark == "BIMBCC_HEAT_LOSS_CUBE" || mark.StartsWith("BCC_HL")))
                                return true;
                        }

                        return false;
                    })
                    .ToList();

                if (cubes.Count == 0)
                {
                    errorMessage = "Не найдено размещенных элементов теплопотерь в текущем проекте.\nУбедитесь, что шаг расстановки кубиков был выполнен.";
                    return false;
                }

                // 2. Сортировка: Номер помещения -> Имя помещения -> Обозначение конструкции -> Площадь
                var sortedCubes = cubes.OrderBy(c => GetParamString(c, HeatLossEngine.P_ROOM_NUMBER))
                                       .ThenBy(c => GetParamString(c, HeatLossEngine.P_ROOM_NAME))
                                       .ThenBy(c => GetParamString(c, HeatLossEngine.P_CONSTR_LABEL))
                                       .ThenBy(c => GetParamDouble(c, HeatLossEngine.P_AREA))
                                       .ToList();

                // 3. Генерация OpenXML .xlsx файла через ZipArchive
                CreateOpenXmlSpreadsheet(sortedCubes, doc.Title, filePath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void CreateOpenXmlSpreadsheet(List<FamilyInstance> cubes, string docTitle, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // [Content_Types].xml
                AddZipEntry(zip, "[Content_Types].xml", GetContentTypesXml());

                // _rels/.rels
                AddZipEntry(zip, "_rels/.rels", GetRootRelsXml());

                // xl/_rels/workbook.xml.rels
                AddZipEntry(zip, "xl/_rels/workbook.xml.rels", GetWorkbookRelsXml());

                // xl/workbook.xml
                AddZipEntry(zip, "xl/workbook.xml", GetWorkbookXml());

                // xl/styles.xml
                AddZipEntry(zip, "xl/styles.xml", GetStylesXml());

                // xl/worksheets/sheet1.xml
                AddZipEntry(zip, "xl/worksheets/sheet1.xml", BuildWorksheetXml(cubes, docTitle));
            }
        }

        private static void AddZipEntry(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string GetContentTypesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                   "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                   "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                   "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                   "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\r\n" +
                   "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\r\n" +
                   "  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\r\n" +
                   "</Types>";
        }

        private static string GetRootRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                   "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\r\n" +
                   "</Relationships>";
        }

        private static string GetWorkbookRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                   "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\r\n" +
                   "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\r\n" +
                   "</Relationships>";
        }

        private static string GetWorkbookXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n" +
                   "  <sheets>\r\n" +
                   "    <sheet name=\"Теплопотери\" sheetId=\"1\" r:id=\"rId1\"/>\r\n" +
                   "  </sheets>\r\n" +
                   "</workbook>";
        }

        private static string GetStylesXml()
        {
            // Стили:
            // 0: Normal
            // 1: Title (Bold 14pt, Red #C91414)
            // 2: Subtitle (Italic 10pt)
            // 3: Header (Bold 11pt, White on #1F497D, Center, Border)
            // 4: Center Text (Border)
            // 5: Left Text (Border)
            // 6: Number 0.0 (Right, Border)
            // 7: Number 0.00 (Right, Border)
            // 8: Number 0.000 (Right, Border)
            // 9: Number #,##0.00 (Right, Border)
            // 10: Zebra Center Text (Fill #F9FAFC)
            // 11: Zebra Left Text (Fill #F9FAFC)
            // 12: Zebra Number 0.0
            // 13: Zebra Number 0.00
            // 14: Zebra Number 0.000
            // 15: Zebra Number #,##0.00
            // 16: Total Label (Bold, Fill #EAEDED, Top/Bottom Border)
            // 17: Total Number (Bold, Number #,##0.00, Fill #EAEDED, Top/Bottom Border)
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\r\n" +
                   "  <numFmts count=\"4\">\r\n" +
                   "    <numFmt numFmtId=\"164\" formatCode=\"0.0\"/>\r\n" +
                   "    <numFmt numFmtId=\"165\" formatCode=\"0.00\"/>\r\n" +
                   "    <numFmt numFmtId=\"166\" formatCode=\"0.000\"/>\r\n" +
                   "    <numFmt numFmtId=\"167\" formatCode=\"#,##0.00\"/>\r\n" +
                   "  </numFmts>\r\n" +
                   "  <fonts count=\"5\">\r\n" +
                   "    <font><sz val=\"11\"/><name val=\"Calibri\"/></font>\r\n" +
                   "    <font><b/><sz val=\"14\"/><color rgb=\"FFC91414\"/><name val=\"Calibri\"/></font>\r\n" +
                   "    <font><i/><sz val=\"10\"/><color rgb=\"FF555555\"/><name val=\"Calibri\"/></font>\r\n" +
                   "    <font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>\r\n" +
                   "    <font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>\r\n" +
                   "  </fonts>\r\n" +
                   "  <fills count=\"5\">\r\n" +
                   "    <fill><patternFill patternType=\"none\"/></fill>\r\n" +
                   "    <fill><patternFill patternType=\"gray125\"/></fill>\r\n" +
                   "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F497D\"/></patternFill></fill>\r\n" +
                   "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF9FAFC\"/></patternFill></fill>\r\n" +
                   "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEAEDED\"/></patternFill></fill>\r\n" +
                   "  </fills>\r\n" +
                   "  <borders count=\"3\">\r\n" +
                   "    <border><left/><right/><top/><bottom/><diagonal/></border>\r\n" +
                   "    <border><left style=\"thin\"><color rgb=\"FFD0D0D0\"/></left><right style=\"thin\"><color rgb=\"FFD0D0D0\"/></right><top style=\"thin\"><color rgb=\"FFD0D0D0\"/></top><bottom style=\"thin\"><color rgb=\"FFD0D0D0\"/></bottom></border>\r\n" +
                   "    <border><left/><right/><top style=\"medium\"><color rgb=\"FF2C3E50\"/></top><bottom style=\"double\"><color rgb=\"FF2C3E50\"/></bottom></border>\r\n" +
                   "  </borders>\r\n" +
                   "  <cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>\r\n" +
                   "  <cellXfs count=\"18\">\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"3\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"167\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"164\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"165\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"166\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"167\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"0\" fontId=\"4\" fillId=\"4\" borderId=\"2\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "    <xf numFmtId=\"167\" fontId=\"4\" fillId=\"4\" borderId=\"2\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n" +
                   "  </cellXfs>\r\n" +
                   "</styleSheet>";
        }

        private static string BuildWorksheetXml(List<FamilyInstance> cubes, string docTitle)
        {
            var sb = new StringBuilder(16384);
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");

            // Sheet Views (Freeze Header at row 4)
            sb.AppendLine("  <sheetViews>");
            sb.AppendLine("    <sheetView tabSelected=\"1\" workbookViewId=\"0\">");
            sb.AppendLine("      <pane ySplit=\"4\" topLeftCell=\"A5\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.AppendLine("    </sheetView>");
            sb.AppendLine("  </sheetViews>");

            // Column Widths
            sb.AppendLine("  <cols>");
            sb.AppendLine("    <col min=\"1\" max=\"1\" width=\"18\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"2\" max=\"2\" width=\"28\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"3\" max=\"4\" width=\"13\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"5\" max=\"5\" width=\"16\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"6\" max=\"6\" width=\"16\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"7\" max=\"7\" width=\"14\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"8\" max=\"8\" width=\"16\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"9\" max=\"9\" width=\"13\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"10\" max=\"10\" width=\"18\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"11\" max=\"14\" width=\"14\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"15\" max=\"15\" width=\"16\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"16\" max=\"16\" width=\"20\" customWidth=\"1\"/>");
            sb.AppendLine("  </cols>");

            sb.AppendLine("  <sheetData>");

            // Row 1: Title
            sb.AppendLine("    <row r=\"1\" ht=\"24\" customHeight=\"1\">");
            sb.AppendLine("      <c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>BIMBCC | Расчёт теплопотерь ограждающих конструкций</t></is></c>");
            sb.AppendLine("    </row>");

            // Row 2: Subtitle
            string subText = EscapeXml($"Проект: {docTitle} | Дата экспорта: {DateTime.Now:dd.MM.yyyy HH:mm}");
            sb.AppendLine("    <row r=\"2\" ht=\"18\" customHeight=\"1\">");
            sb.AppendLine($"      <c r=\"A2\" t=\"inlineStr\" s=\"2\"><is><t>{subText}</t></is></c>");
            sb.AppendLine("    </row>");

            // Row 3: Empty
            sb.AppendLine("    <row r=\"3\" ht=\"10\" customHeight=\"1\"/>");

            // Row 4: Table Headers
            sb.AppendLine("    <row r=\"4\" ht=\"28\" customHeight=\"1\">");
            string[] headers = new[]
            {
                "Номер помещения",
                "Имя помещения",
                "t нар, °C",
                "t вн, °C",
                "Тип помещения",
                "Конструкция",
                "Ориентация",
                "Площадь A, м²",
                "Коэфф. n",
                "Коэфф. k, Вт/(м²·°C)",
                "Надбавка b1",
                "Надбавка b2",
                "Надбавка b3",
                "Надбавка b4",
                "Коэфф. надбавки",
                "Теплопотери Q, Вт"
            };

            for (int c = 0; c < headers.Length; c++)
            {
                string colLetter = GetColumnLetter(c + 1);
                sb.AppendLine($"      <c r=\"{colLetter}4\" t=\"inlineStr\" s=\"3\"><is><t>{EscapeXml(headers[c])}</t></is></c>");
            }
            sb.AppendLine("    </row>");

            // Data Rows
            int startRow = 5;
            int r = startRow;
            var ci = CultureInfo.InvariantCulture;

            foreach (var cube in cubes)
            {
                bool isZebra = ((r - startRow) % 2 == 1);
                int sCenter = isZebra ? 10 : 4;
                int sLeft   = isZebra ? 11 : 5;
                int sNum1   = isZebra ? 12 : 6; // 0.0
                int sNum2   = isZebra ? 13 : 7; // 0.00
                int sNum3   = isZebra ? 14 : 8; // 0.000
                int sNumQ   = isZebra ? 15 : 9; // #,##0.00

                string roomNum     = EscapeXml(GetParamString(cube, HeatLossEngine.P_ROOM_NUMBER));
                string roomName    = EscapeXml(GetParamString(cube, HeatLossEngine.P_ROOM_NAME));
                double tempOut     = GetParamDouble(cube, HeatLossEngine.P_TEMP_OUT);
                double tempIn      = GetParamDouble(cube, HeatLossEngine.P_TEMP_IN);
                string cornerType  = EscapeXml(GetParamString(cube, HeatLossEngine.P_CORNER_TYPE));
                string constrLabel = EscapeXml(GetParamString(cube, HeatLossEngine.P_CONSTR_LABEL));
                string orient      = EscapeXml(GetParamString(cube, HeatLossEngine.P_ORIENTATION));
                double area        = GetParamDouble(cube, HeatLossEngine.P_AREA);
                double coeffN      = GetParamDouble(cube, HeatLossEngine.P_COEFF_N);
                double coeffK      = GetParamDouble(cube, HeatLossEngine.P_COEFF_K);
                double b1          = GetParamDouble(cube, HeatLossEngine.P_ADD_B1);
                double b2          = GetParamDouble(cube, HeatLossEngine.P_ADD_B2);
                double b3          = GetParamDouble(cube, HeatLossEngine.P_ADD_B3);
                double b4          = GetParamDouble(cube, HeatLossEngine.P_ADD_B4);

                double coeffAddCalc = Math.Round(1.0 + b1 + b2 + b3 + b4, 4);
                double qCalc = Math.Round(area * coeffK * (tempIn - tempOut) * coeffN * coeffAddCalc, 2);

                sb.AppendLine($"    <row r=\"{r}\" ht=\"20\" customHeight=\"1\">");
                sb.AppendLine($"      <c r=\"A{r}\" t=\"inlineStr\" s=\"{sCenter}\"><is><t>{roomNum}</t></is></c>");
                sb.AppendLine($"      <c r=\"B{r}\" t=\"inlineStr\" s=\"{sLeft}\"><is><t>{roomName}</t></is></c>");
                sb.AppendLine($"      <c r=\"C{r}\" s=\"{sNum1}\"><v>{tempOut.ToString("F1", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"D{r}\" s=\"{sNum1}\"><v>{tempIn.ToString("F1", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"E{r}\" t=\"inlineStr\" s=\"{sCenter}\"><is><t>{cornerType}</t></is></c>");
                sb.AppendLine($"      <c r=\"F{r}\" t=\"inlineStr\" s=\"{sCenter}\"><is><t>{constrLabel}</t></is></c>");
                sb.AppendLine($"      <c r=\"G{r}\" t=\"inlineStr\" s=\"{sCenter}\"><is><t>{orient}</t></is></c>");
                sb.AppendLine($"      <c r=\"H{r}\" s=\"{sNum2}\"><v>{area.ToString("F2", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"I{r}\" s=\"{sNum1}\"><v>{coeffN.ToString("F1", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"J{r}\" s=\"{sNum3}\"><v>{coeffK.ToString("F3", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"K{r}\" s=\"{sNum2}\"><v>{b1.ToString("F2", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"L{r}\" s=\"{sNum2}\"><v>{b2.ToString("F2", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"M{r}\" s=\"{sNum2}\"><v>{b3.ToString("F2", ci)}</v></c>");
                sb.AppendLine($"      <c r=\"N{r}\" s=\"{sNum2}\"><v>{b4.ToString("F2", ci)}</v></c>");

                // ФОРМУЛА: Коэффициент надбавки (O) = 1 + b1 + b2 + b3 + b4
                sb.AppendLine($"      <c r=\"O{r}\" s=\"{sNum2}\"><f>1+K{r}+L{r}+M{r}+N{r}</f><v>{coeffAddCalc.ToString("F2", ci)}</v></c>");

                // ФОРМУЛА: Теплопотери Q (P) = ROUND(H * J * (D - C) * I * O, 2)
                sb.AppendLine($"      <c r=\"P{r}\" s=\"{sNumQ}\"><f>ROUND(H{r}*J{r}*(D{r}-C{r})*I{r}*O{r}, 2)</f><v>{qCalc.ToString("F2", ci)}</v></c>");

                sb.AppendLine("    </row>");
                r++;
            }

            int lastDataRow = r - 1;
            int totalRow = r;

            // Строка ИТОГО ПО ЗДАНИЮ
            sb.AppendLine($"    <row r=\"{totalRow}\" ht=\"24\" customHeight=\"1\">");
            sb.AppendLine($"      <c r=\"A{totalRow}\" t=\"inlineStr\" s=\"16\"><is><t>ИТОГО ПО ЗДАНИЮ:</t></is></c>");
            sb.AppendLine($"      <c r=\"B{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"C{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"D{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"E{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"F{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"G{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"H{totalRow}\" s=\"17\"><f>SUM(H{startRow}:H{lastDataRow})</f></c>");
            sb.AppendLine($"      <c r=\"I{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"J{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"K{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"L{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"M{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"N{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"O{totalRow}\" s=\"16\"/>");
            sb.AppendLine($"      <c r=\"P{totalRow}\" s=\"17\"><f>SUM(P{startRow}:P{lastDataRow})</f></c>");
            sb.AppendLine("    </row>");

            sb.AppendLine("  </sheetData>");

            // Merge cells for Total label A{totalRow}:G{totalRow}
            sb.AppendLine("  <mergeCells count=\"1\">");
            sb.AppendLine($"    <mergeCell ref=\"A{totalRow}:G{totalRow}\"/>");
            sb.AppendLine("  </mergeCells>");

            sb.AppendLine("</worksheet>");
            return sb.ToString();
        }

        private static string GetColumnLetter(int colIndex)
        {
            int div = colIndex;
            string colLetter = String.Empty;
            while (div > 0)
            {
                int mod = (div - 1) % 26;
                colLetter = (char)(65 + mod) + colLetter;
                div = (int)((div - mod) / 26);
            }
            return colLetter;
        }

        private static string EscapeXml(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }

        private static readonly Dictionary<string, string[]> ParamAliases = new Dictionary<string, string[]>
        {
            { HeatLossEngine.P_ROOM_NUMBER, new[] { HeatLossEngine.P_ROOM_NUMBER, "BIMBCC_SpaceNumber", "BCC_HL_Номер помещения", "Номер помещения" } },
            { HeatLossEngine.P_ROOM_NAME, new[] { HeatLossEngine.P_ROOM_NAME, "BIMBCC_SpaceName", "BCC_HL_Имя помещения", "Имя помещения" } },
            { HeatLossEngine.P_TEMP_OUT, new[] { HeatLossEngine.P_TEMP_OUT, "BIMBCC_OutdoorTemp", "BCC_HL_Температура наружного воздуха", "t_нар" } },
            { HeatLossEngine.P_TEMP_IN, new[] { HeatLossEngine.P_TEMP_IN, "BIMBCC_IndoorTemp", "BCC_HL_Температура внутреннего воздуха", "t_вн" } },
            { HeatLossEngine.P_CORNER_TYPE, new[] { HeatLossEngine.P_CORNER_TYPE, "BIMBCC_CornerType", "BCC_HL_Тип углового помещения" } },
            { HeatLossEngine.P_CONSTR_LABEL, new[] { HeatLossEngine.P_CONSTR_LABEL, "BIMBCC_ConstructionLabel", "BCC_HL_Обозначение конструкции", "Обозначение" } },
            { HeatLossEngine.P_ORIENTATION, new[] { HeatLossEngine.P_ORIENTATION, "BIMBCC_Orientation", "BCC_HL_Ориентация конструкции", "Ориентация" } },
            { HeatLossEngine.P_AREA, new[] { HeatLossEngine.P_AREA, "BIMBCC_Area", "BCC_HL_Площадь конструкции", "Площадь" } },
            { HeatLossEngine.P_COEFF_N, new[] { HeatLossEngine.P_COEFF_N, "BIMBCC_CoeffN", "BCC_HL_Коэффициент n", "Коэффициент n" } },
            { HeatLossEngine.P_COEFF_K, new[] { HeatLossEngine.P_COEFF_K, "BIMBCC_CoeffK", "BCC_HL_Коэффициент теплопередачи k", "Коэффициент k" } },
            { HeatLossEngine.P_ADD_B1, new[] { HeatLossEngine.P_ADD_B1, "BIMBCC_B1", "BCC_HL_Надбавка b1", "Надбавка b1" } },
            { HeatLossEngine.P_ADD_B2, new[] { HeatLossEngine.P_ADD_B2, "BIMBCC_B2", "BCC_HL_Надбавка b2", "Надбавка b2" } },
            { HeatLossEngine.P_ADD_B3, new[] { HeatLossEngine.P_ADD_B3, "BIMBCC_B3", "BCC_HL_Надбавка b3", "Надбавка b3" } },
            { HeatLossEngine.P_ADD_B4, new[] { HeatLossEngine.P_ADD_B4, "BIMBCC_B4", "BCC_HL_Надбавка b4", "Надбавка b4" } },
            { HeatLossEngine.P_COEFF_ADD, new[] { HeatLossEngine.P_COEFF_ADD, "BIMBCC_CoeffAllow", "BCC_HL_Коэффициент надбавки" } },
            { HeatLossEngine.P_HEAT_LOSS, new[] { HeatLossEngine.P_HEAT_LOSS, "BIMBCC_HeatLoss", "BCC_HL_Теплопотери", "Теплопотери" } }
        };

        private static string GetParamString(Element elem, string paramName)
        {
            if (elem == null) return "";
            try
            {
                if (ParamAliases.TryGetValue(paramName, out string[] aliases))
                {
                    foreach (string alias in aliases)
                    {
                        Parameter p = elem.LookupParameter(alias);
                        if (p != null && p.HasValue)
                        {
                            if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                            return p.AsValueString() ?? "";
                        }
                    }
                }
                else
                {
                    Parameter p = elem.LookupParameter(paramName);
                    if (p != null && p.HasValue)
                    {
                        if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                        return p.AsValueString() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        private static double GetParamDouble(Element elem, string paramName)
        {
            if (elem == null) return 0.0;
            try
            {
                if (ParamAliases.TryGetValue(paramName, out string[] aliases))
                {
                    foreach (string alias in aliases)
                    {
                        Parameter p = elem.LookupParameter(alias);
                        if (p != null && p.HasValue)
                        {
                            if (p.StorageType == StorageType.Double) return Math.Round(p.AsDouble(), 4);
                            if (p.StorageType == StorageType.Integer) return p.AsInteger();
                            if (double.TryParse(p.AsString().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                return val;
                        }
                    }
                }
                else
                {
                    Parameter p = elem.LookupParameter(paramName);
                    if (p != null && p.HasValue)
                    {
                        if (p.StorageType == StorageType.Double) return Math.Round(p.AsDouble(), 4);
                        if (p.StorageType == StorageType.Integer) return p.AsInteger();
                        if (double.TryParse(p.AsString().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                            return val;
                    }
                }
            }
            catch { }
            return 0.0;
        }
    }
}
