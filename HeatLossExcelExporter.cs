using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;

namespace BCCPlugIn
{
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
                        Parameter pMark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        return pMark != null && pMark.HasValue && pMark.AsString() == "BIMBCC_HEAT_LOSS_CUBE";
                    })
                    .ToList();

                if (cubes.Count == 0)
                {
                    errorMessage = "Не найдено размещенных элементов теплопотерь для экспорта.";
                    return false;
                }

                // 2. Сортировка: Номер помещения -> Имя помещения -> Обозначение конструкции -> Площадь
                var sortedCubes = cubes.OrderBy(c => GetParamString(c, HeatLossEngine.P_ROOM_NUMBER))
                                       .ThenBy(c => GetParamString(c, HeatLossEngine.P_ROOM_NAME))
                                       .ThenBy(c => GetParamString(c, HeatLossEngine.P_CONSTR_LABEL))
                                       .ThenBy(c => GetParamDouble(c, HeatLossEngine.P_AREA))
                                       .ToList();

                // 3. Создание книги Excel с помощью ClosedXML
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Теплопотери");

                    // Заголовок листа
                    ws.Cell("A1").Value = "BIMBCC | Расчёт теплопотерь ограждающих конструкций";
                    ws.Cell("A1").Style.Font.Bold = true;
                    ws.Cell("A1").Style.Font.FontSize = 14;
                    ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#C91414");

                    string docTitle = doc.Title;
                    ws.Cell("A2").Value = $"Проект: {docTitle} | Дата экспорта: {DateTime.Now:dd.MM.yyyy HH:mm}";
                    ws.Cell("A2").Style.Font.Italic = true;
                    ws.Cell("A2").Style.Font.FontSize = 10;
                    ws.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#555555");

                    // Шапка таблицы (Строка 4)
                    int headerRow = 4;
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
                        var cell = ws.Cell(headerRow, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontSize = 11;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D"); // Элегантный синий корпоративный цвет
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Alignment.WrapText = true;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B0B0B0");
                    }
                    ws.Row(headerRow).Height = 28;

                    // Заполнение строк данных
                    int startDataRow = 5;
                    int currentRow = startDataRow;

                    foreach (var cube in sortedCubes)
                    {
                        string roomNum     = GetParamString(cube, HeatLossEngine.P_ROOM_NUMBER);
                        string roomName    = GetParamString(cube, HeatLossEngine.P_ROOM_NAME);
                        double tempOut     = GetParamDouble(cube, HeatLossEngine.P_TEMP_OUT);
                        double tempIn      = GetParamDouble(cube, HeatLossEngine.P_TEMP_IN);
                        string cornerType  = GetParamString(cube, HeatLossEngine.P_CORNER_TYPE);
                        string constrLabel = GetParamString(cube, HeatLossEngine.P_CONSTR_LABEL);
                        string orient      = GetParamString(cube, HeatLossEngine.P_ORIENTATION);
                        double area        = GetParamDouble(cube, HeatLossEngine.P_AREA);
                        double coeffN      = GetParamDouble(cube, HeatLossEngine.P_COEFF_N);
                        double coeffK      = GetParamDouble(cube, HeatLossEngine.P_COEFF_K);
                        double b1          = GetParamDouble(cube, HeatLossEngine.P_ADD_B1);
                        double b2          = GetParamDouble(cube, HeatLossEngine.P_ADD_B2);
                        double b3          = GetParamDouble(cube, HeatLossEngine.P_ADD_B3);
                        double b4          = GetParamDouble(cube, HeatLossEngine.P_ADD_B4);

                        // Значения колонок 1..14
                        ws.Cell(currentRow, 1).Value  = roomNum;
                        ws.Cell(currentRow, 2).Value  = roomName;
                        ws.Cell(currentRow, 3).Value  = tempOut;
                        ws.Cell(currentRow, 4).Value  = tempIn;
                        ws.Cell(currentRow, 5).Value  = cornerType;
                        ws.Cell(currentRow, 6).Value  = constrLabel;
                        ws.Cell(currentRow, 7).Value  = orient;
                        ws.Cell(currentRow, 8).Value  = area;
                        ws.Cell(currentRow, 9).Value  = coeffN;
                        ws.Cell(currentRow, 10).Value = coeffK;
                        ws.Cell(currentRow, 11).Value = b1;
                        ws.Cell(currentRow, 12).Value = b2;
                        ws.Cell(currentRow, 13).Value = b3;
                        ws.Cell(currentRow, 14).Value = b4;

                        // ФОРМУЛА: Коэффициент надбавки (Колонка 15 / O) = 1 + b1 + b2 + b3 + b4
                        ws.Cell(currentRow, 15).FormulaA1 = $"=1+K{currentRow}+L{currentRow}+M{currentRow}+N{currentRow}";

                        // ФОРМУЛА: Теплопотери Q (Колонка 16 / P) = ROUND(A * k * (t_in - t_out) * n * coeff_add, 2)
                        ws.Cell(currentRow, 16).FormulaA1 = $"=ROUND(H{currentRow}*J{currentRow}*(D{currentRow}-C{currentRow})*I{currentRow}*O{currentRow}, 2)";

                        // Форматирование ячеек строки
                        for (int col = 1; col <= 16; col++)
                        {
                            var cell = ws.Cell(currentRow, col);
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E0E0E0");

                            // Текстовые столбцы выравниваем по центру / влево
                            if (col == 1 || col == 3 || col == 4 || col == 5 || col == 6 || col == 7)
                            {
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            }
                            else if (col == 2)
                            {
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                            }
                            else
                            {
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            }
                        }

                        // Числовые форматы
                        ws.Cell(currentRow, 3).Style.NumberFormat.Format  = "0.0";
                        ws.Cell(currentRow, 4).Style.NumberFormat.Format  = "0.0";
                        ws.Cell(currentRow, 8).Style.NumberFormat.Format  = "0.00";
                        ws.Cell(currentRow, 9).Style.NumberFormat.Format  = "0.0";
                        ws.Cell(currentRow, 10).Style.NumberFormat.Format = "0.000";
                        ws.Cell(currentRow, 11).Style.NumberFormat.Format = "0.00";
                        ws.Cell(currentRow, 12).Style.NumberFormat.Format = "0.00";
                        ws.Cell(currentRow, 13).Style.NumberFormat.Format = "0.00";
                        ws.Cell(currentRow, 14).Style.NumberFormat.Format = "0.00";
                        ws.Cell(currentRow, 15).Style.NumberFormat.Format = "0.00";
                        ws.Cell(currentRow, 16).Style.NumberFormat.Format = "#,##0.00";

                        // Зебра строк для удобства чтения
                        if ((currentRow - startDataRow) % 2 == 1)
                        {
                            ws.Range(currentRow, 1, currentRow, 16).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFC");
                        }

                        currentRow++;
                    }

                    int lastDataRow = currentRow - 1;

                    // Строка ИТОГО ПО ЗДАНИЮ
                    int totalRow = currentRow;
                    ws.Range(totalRow, 1, totalRow, 7).Merge();
                    ws.Cell(totalRow, 1).Value = "ИТОГО ПО ЗДАНИЮ:";
                    ws.Cell(totalRow, 1).Style.Font.Bold = true;
                    ws.Cell(totalRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Формула суммы площадей
                    ws.Cell(totalRow, 8).FormulaA1 = $"=SUM(H{startDataRow}:H{lastDataRow})";
                    ws.Cell(totalRow, 8).Style.Font.Bold = true;
                    ws.Cell(totalRow, 8).Style.NumberFormat.Format = "#,##0.00";

                    // Формула суммы теплопотерь Q
                    ws.Cell(totalRow, 16).FormulaA1 = $"=SUM(P{startDataRow}:P{lastDataRow})";
                    ws.Cell(totalRow, 16).Style.Font.Bold = true;
                    ws.Cell(totalRow, 16).Style.NumberFormat.Format = "#,##0.00";

                    // Стилизация строки итогов
                    var totalRange = ws.Range(totalRow, 1, totalRow, 16);
                    totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAEDED");
                    totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                    totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                    totalRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#2C3E50");

                    // Автоподбор ширины столбцов
                    ws.Columns().AdjustToContents(startDataRow - 1, totalRow);

                    // Установим минимальную комфортную ширину
                    ws.Column(1).Width = Math.Max(ws.Column(1).Width, 16);
                    ws.Column(2).Width = Math.Max(ws.Column(2).Width, 24);
                    ws.Column(8).Width = Math.Max(ws.Column(8).Width, 14);
                    ws.Column(10).Width = Math.Max(ws.Column(10).Width, 16);
                    ws.Column(15).Width = Math.Max(ws.Column(15).Width, 15);
                    ws.Column(16).Width = Math.Max(ws.Column(16).Width, 18);

                    // Закрепить шапку
                    ws.SheetView.FreezeRows(headerRow);

                    // Сохранить файл
                    if (File.Exists(filePath)) File.Delete(filePath);
                    workbook.SaveAs(filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string GetParamString(Element elem, string paramName)
        {
            if (elem == null) return "";
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                    return p.AsValueString() ?? "";
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
                Parameter p = elem.LookupParameter(paramName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Double) return Math.Round(p.AsDouble(), 4);
                    if (p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (double.TryParse(p.AsString().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        return val;
                }
            }
            catch { }
            return 0.0;
        }
    }
}
