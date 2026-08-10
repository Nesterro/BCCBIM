using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportPdfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            try
            {
                // Find all printable sheets
                List<ViewSheet> sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .ToList();

                if (sheets.Count == 0)
                {
                    MessageBox.Show("В текущем проекте не найдено листов для экспорта.", "Экспорт PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                    return Result.Succeeded;
                }

                // Find all sheet sets
                List<ViewSheetSet> sheetSets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .OrderBy(s => s.Name)
                    .ToList();

                // Determine default export path
                string defaultFolder = GetDefaultExportDirectory(doc);

                // Show WPF dialog
                ExportWindow window = new ExportWindow(sheets, sheetSets, defaultFolder);
                
                // Set owner window to Revit Main Window
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;

                if (window.ShowDialog() == true)
                {
                    string folderPath = window.SelectedFolderPath;
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    List<ElementId> selectedSheetIds = window.AllSheets
                        .Where(s => s.IsSelected)
                        .Select(s => s.Sheet.Id)
                        .ToList();

                    bool isNativePdf = string.Equals(window.SelectedPrinterName, "Встроенный экспорт Revit PDF (Как в DiRoots)", StringComparison.OrdinalIgnoreCase);

                    // Open Progress window
                    ProgressWindow progress = new ProgressWindow();
                    WindowInteropHelper progressHelper = new WindowInteropHelper(progress);
                    progressHelper.Owner = uiapp.MainWindowHandle;
                    progress.Show();

                    if (isNativePdf)
                    {
                        // --------------------------------------------------------
                        // NATIVE REVIT 2024 PDF EXPORT ENGINE (DiRoots Style)
                        // --------------------------------------------------------
                        progress.UpdateProgress("Экспорт листов через встроенный движок Revit PDF...", 50, true);

                        PDFExportOptions pdfOptions = new PDFExportOptions();
                        pdfOptions.Combine = window.CombineSheets;

                        if (window.CombineSheets)
                        {
                            pdfOptions.FileName = SanitizeFileName(window.CombinedFileName);
                        }

                        // Paper Format
                        if (window.SelectedPaperFormat != ExportPaperFormat.Default)
                        {
                            pdfOptions.PaperFormat = window.SelectedPaperFormat;
                        }

                        // Color Depth
                        pdfOptions.ColorDepth = window.SelectedColorDepth;

                        // Crop Margins & Zoom
                        if (window.CropMargins)
                        {
                            pdfOptions.ZoomType = ZoomType.Zoom;
                            pdfOptions.ZoomPercentage = 100;
                        }
                        else
                        {
                            pdfOptions.ZoomType = ZoomType.FitToPage;
                        }

                        // Raster vs Vector
                        if (window.AlwaysUseRaster)
                        {
                            pdfOptions.ExportQuality = PDFExportQualityType.DPI300;
                        }

                        // Hide unneeded elements
                        pdfOptions.HideUnreferencedViewTags = true;
                        pdfOptions.HideScopeBoxes = true;
                        pdfOptions.HideCropBoundaries = true;

                        // Execute native export
                        using (Transaction trans = new Transaction(doc, "BIMBCC Native PDF Export"))
                        {
                            trans.Start();

                            if (window.FixQuotes)
                            {
                                FixQuotesInDocument(doc, selectedSheetIds);
                                doc.Regenerate();
                            }

                            doc.Export(folderPath, selectedSheetIds, pdfOptions);

                            if (window.FixQuotes)
                            {
                                trans.RollBack(); // Revert in-memory changes so Revit model stays untouched!
                            }
                            else
                            {
                                trans.Commit();
                            }
                        }

                        progress.Close();
                    }
                    else
                    {
                        // --------------------------------------------------------
                        // GDI PRINTER EXPORT ENGINE (PDF24 / System Printers)
                        // --------------------------------------------------------
                        PrintManager printManager = doc.PrintManager;
                        try
                        {
                            printManager.SelectNewPrintDriver(window.SelectedPrinterName);
                        }
                        catch (Exception ex)
                        {
                            progress.Close();
                            MessageBox.Show($"Не удалось выбрать принтер '{window.SelectedPrinterName}':\n{ex.Message}", "Ошибка инициализации принтера", MessageBoxButton.OK, MessageBoxImage.Error);
                            return Result.Failed;
                        }

                        bool isPdf24 = window.SelectedPrinterName.IndexOf("PDF24", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (window.CombineSheets)
                        {
                            progress.UpdateProgress("Печать объединенного PDF файла...", 0, true);

                            string fileName = SanitizeFileName(window.CombinedFileName);
                            string filePath = Path.Combine(folderPath, fileName + ".pdf");

                            if (File.Exists(filePath))
                            {
                                try { File.Delete(filePath); } catch { }
                            }

                            // Setup print settings inside transaction
                            using (Transaction trans = new Transaction(doc, "BIMBCC Print Configuration"))
                            {
                                trans.Start();

                                printManager.PrintRange = PrintRange.Select;

                                ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
                                ViewSet views = new ViewSet();
                                foreach (var id in selectedSheetIds)
                                {
                                    ViewSheet sheet = doc.GetElement(id) as ViewSheet;
                                    if (sheet != null) views.Insert(sheet);
                                }
                                viewSheetSetting.CurrentViewSheetSet.Views = views;
                                
                                try
                                {
                                    viewSheetSetting.Save();
                                }
                                catch
                                {
                                    try { viewSheetSetting.SaveAs("BIMBCC_Temp_Set"); } catch { }
                                }

                                PrintParameters printParams = printManager.PrintSetup.CurrentPrintSetting.PrintParameters;
                                ConfigurePrintParameters(printParams, window, printManager);

                                printManager.PrintToFile = true;
                                printManager.CombinedFile = true;
                                printManager.PrintToFileName = filePath;
                                printManager.Apply();

                                trans.Commit();
                            }

                            if (isPdf24)
                            {
                                ConfigurePdf24SilentPrint(window.SelectedPrinterName, folderPath, fileName);
                            }

                            // Submit print job (outside transaction)
                            printManager.SubmitPrint();

                            if (isPdf24)
                            {
                                int elapsedMs = 0;
                                while (!File.Exists(filePath) && elapsedMs < 15000)
                                {
                                    System.Threading.Thread.Sleep(200);
                                    elapsedMs += 200;
                                }
                            }
                        }
                        else
                        {
                            // Print sheet-by-sheet
                            int total = selectedSheetIds.Count;
                            for (int i = 0; i < total; i++)
                            {
                                var sheetId = selectedSheetIds[i];
                                ViewSheet sheet = doc.GetElement(sheetId) as ViewSheet;
                                if (sheet == null) continue;

                                double pct = ((double)(i + 1) / total) * 100;
                                progress.UpdateProgress($"Печать листа {i + 1} из {total}: {sheet.SheetNumber} - {sheet.Name}...", pct);

                                string sheetFileName = SanitizeFileName($"{sheet.SheetNumber} - {sheet.Name}");
                                string filePath = Path.Combine(folderPath, sheetFileName + ".pdf");

                                if (File.Exists(filePath))
                                {
                                    try { File.Delete(filePath); } catch { }
                                }

                                using (Transaction trans = new Transaction(doc, "BIMBCC Print Single Sheet"))
                                {
                                    trans.Start();

                                    printManager.PrintRange = PrintRange.Select;

                                    ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
                                    ViewSet views = new ViewSet();
                                    views.Insert(sheet);
                                    viewSheetSetting.CurrentViewSheetSet.Views = views;
                                    
                                    try
                                    {
                                        viewSheetSetting.Save();
                                    }
                                    catch
                                    {
                                        try { viewSheetSetting.SaveAs("BIMBCC_Temp_Set"); } catch { }
                                    }

                                    PrintParameters printParams = printManager.PrintSetup.CurrentPrintSetting.PrintParameters;
                                    ConfigurePrintParameters(printParams, window, printManager);

                                    printManager.PrintToFile = true;
                                    printManager.CombinedFile = true;
                                    printManager.PrintToFileName = filePath;
                                    printManager.Apply();

                                    trans.Commit();
                                }

                                if (isPdf24)
                                {
                                    ConfigurePdf24SilentPrint(window.SelectedPrinterName, folderPath, sheetFileName);
                                }

                                printManager.SubmitPrint();

                                if (isPdf24)
                                {
                                    int elapsedMs = 0;
                                    while (!File.Exists(filePath) && elapsedMs < 10000)
                                    {
                                        System.Threading.Thread.Sleep(100);
                                        elapsedMs += 100;
                                    }
                                }
                            }
                        }

                        progress.Close();
                    }

                    // Prompt to open directory
                    var openFolderResult = MessageBox.Show(
                        "Экспорт успешно завершен!\n\nОткрыть папку с напечатанными PDF файлами?",
                        "BIMBCC | Экспорт PDF",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (openFolderResult == MessageBoxResult.Yes)
                    {
                        try
                        {
                            if (Directory.Exists(folderPath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Не удалось открыть папку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }

                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show($"Произошла ошибка при экспорте:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }

        private void ConfigurePdf24SilentPrint(string printerName, string folderPath, string fileNameWithoutExtension)
        {
            try
            {
                // 1. Write directly to Registry
                List<string> regPaths = new List<string>
                {
                    @"SOFTWARE\PDF24\PDFPrinters\pdf24",
                    @"SOFTWARE\PDF24\PDFPrinter",
                    @"SOFTWARE\PDF24\Desktop",
                    @"SOFTWARE\PDF24\autoSave",
                    @"SOFTWARE\PDF24",
                    $@"SOFTWARE\PDF24\Printers\{printerName}",
                    $@"SOFTWARE\PDF24\Printers\{printerName.ToLower()}",
                    $@"SOFTWARE\PDF24\Printers\{printerName.ToUpper()}",
                    $@"SOFTWARE\PDF24\PDFPrinters\{printerName}",
                    $@"SOFTWARE\PDF24\PDFPrinters\{printerName.ToLower()}",
                    $@"SOFTWARE\PDF24\PDFPrinters\{printerName.ToUpper()}",
                    $@"SOFTWARE\PDF24\Services\{printerName}",
                    $@"SOFTWARE\PDFPrint\Printers\{printerName}",
                    $@"SOFTWARE\PDFPrint\Services\{printerName}"
                };

                foreach (var path in regPaths)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path))
                    {
                        if (key != null)
                        {
                            key.SetValue("autoSave", "1", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSave", "1", Microsoft.Win32.RegistryValueKind.String);
                            
                            key.SetValue("autoSaveDir", folderPath, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSaveDir", folderPath, Microsoft.Win32.RegistryValueKind.String);
                            
                            key.SetValue("autoSaveFilename", fileNameWithoutExtension, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSaveFilename", fileNameWithoutExtension, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoFileName", fileNameWithoutExtension, Microsoft.Win32.RegistryValueKind.String);
                            
                            key.SetValue("showAssistant", "0", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("ShowAssistant", "0", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("openAssistant", "0", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("OpenAssistant", "0", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("assistant", "0", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("Assistant", "0", Microsoft.Win32.RegistryValueKind.String);

                            key.SetValue("autoSaveOutputFormat", "pdf", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSaveOutputFormat", "pdf", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("autoSaveMode", "auto", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSaveMode", "auto", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("autoSaveProfile", "default", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("AutoSaveProfile", "default", Microsoft.Win32.RegistryValueKind.String);
                        }
                    }
                }

                // 2. Write directly to PDF24 AppData ini file
                WritePdf24Ini(folderPath, fileNameWithoutExtension);
            }
            catch
            {
                // Suppress registry errors
            }
        }

        private void WritePdf24Ini(string folderPath, string fileNameWithoutExtension)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string iniPath = Path.Combine(appData, "PDF24", "pdf24.ini");
                string dir = Path.GetDirectoryName(iniPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                List<string> lines = new List<string>();
                if (File.Exists(iniPath))
                {
                    lines = File.ReadAllLines(iniPath).ToList();
                }

                SetIniKey(lines, "pdfPrinters\\pdf24", "autoSave", "1");
                SetIniKey(lines, "pdfPrinters\\pdf24", "autoSaveDir", folderPath);
                SetIniKey(lines, "pdfPrinters\\pdf24", "autoSaveFilename", fileNameWithoutExtension);
                SetIniKey(lines, "pdfPrinters\\pdf24", "showAssistant", "0");
                SetIniKey(lines, "pdfPrinters\\pdf24", "openAssistant", "0");

                File.WriteAllLines(iniPath, lines);
            }
            catch { }
        }

        private void SetIniKey(List<string> lines, string section, string key, string value)
        {
            int sectionIdx = -1;
            string targetSectionHeader = $"[{section}]";

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals(targetSectionHeader, StringComparison.OrdinalIgnoreCase))
                {
                    sectionIdx = i;
                    break;
                }
            }

            if (sectionIdx == -1)
            {
                lines.Add(targetSectionHeader);
                lines.Add($"{key}={value}");
            }
            else
            {
                bool found = false;
                for (int i = sectionIdx + 1; i < lines.Count; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("[") && line.EndsWith("]")) break;

                    if (line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}={value}";
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    lines.Insert(sectionIdx + 1, $"{key}={value}");
                }
            }
        }

        private void ConfigurePrintParameters(PrintParameters printParams, ExportWindow window, PrintManager printManager)
        {
            printParams.PageOrientation = window.SelectedPageOrientation;

            // Configure margins (crop empty fields)
            if (window.CropMargins)
            {
                printParams.PaperPlacement = PaperPlacementType.Margins;
                printParams.MarginType = MarginType.NoMargin;
                printParams.ZoomType = ZoomType.Zoom;
                printParams.Zoom = 100; // FORCE 1:1 scale to prevent FitToPage margin padding!
                try
                {
                    printParams.OriginOffsetX = 0;
                    printParams.OriginOffsetY = 0;
                }
                catch { }
            }
            else
            {
                printParams.PaperPlacement = PaperPlacementType.Center;
                printParams.ZoomType = ZoomType.FitToPage;
            }

            // Set color
            if (window.SelectedColorDepth == ColorDepthType.Color)
                printParams.ColorDepth = ColorDepthType.Color;
            else if (window.SelectedColorDepth == ColorDepthType.GrayScale)
                printParams.ColorDepth = ColorDepthType.GrayScale;
            else
                printParams.ColorDepth = ColorDepthType.BlackLine;

            // Set AlwaysUseRaster (processing type) using HiddenLineViewsType enum
            if (window.AlwaysUseRaster)
            {
                printParams.HiddenLineViews = HiddenLineViewsType.RasterProcessing;
            }
            else
            {
                printParams.HiddenLineViews = HiddenLineViewsType.VectorProcessing;
            }

            // Map paper format
            string formatName = null;
            if (window.SelectedPaperFormat == ExportPaperFormat.ISO_A4) formatName = "A4";
            else if (window.SelectedPaperFormat == ExportPaperFormat.ISO_A3) formatName = "A3";
            else if (window.SelectedPaperFormat == ExportPaperFormat.ISO_A2) formatName = "A2";
            else if (window.SelectedPaperFormat == ExportPaperFormat.ISO_A1) formatName = "A1";
            else if (window.SelectedPaperFormat == ExportPaperFormat.ISO_A0) formatName = "A0";

            if (formatName != null)
            {
                try
                {
                    PaperSize paperSize = null;
                    foreach (PaperSize ps in printManager.PaperSizes)
                    {
                        if (ps.Name.IndexOf(formatName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            paperSize = ps;
                            break;
                        }
                    }
                    if (paperSize != null)
                    {
                        printParams.PaperSize = paperSize;
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }
        }

        private string SanitizeGostString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Replace Cyrillic quotes « » with " to avoid fraction bugs ½ ¾
            string result = input.Replace("«", "\"").Replace("»", "\"");

            // Replace degree symbol ° (U+00B0) with U+02DA (˚ Ring Above) which maps to true degree circle (Glyph 186) in GOST fonts
            result = result.Replace("°", "\u02DA");

            return result;
        }

        private void FixQuotesInDocument(Document doc, List<ElementId> selectedSheetIds)
        {
            try
            {
                // 1. Fix all TextNotes
                var textNotes = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();

                foreach (var tn in textNotes)
                {
                    string text = tn.Text;
                    if (!string.IsNullOrEmpty(text) && (text.Contains("«") || text.Contains("»") || text.Contains("°")))
                    {
                        tn.Text = SanitizeGostString(text);
                    }
                }

                // 2. Fix Title Block parameters on selected sheets
                var titleBlocks = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (Element tb in titleBlocks)
                {
                    foreach (Parameter p in tb.Parameters)
                    {
                        if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
                        string val = p.AsString();
                        if (!string.IsNullOrEmpty(val) && (val.Contains("«") || val.Contains("»") || val.Contains("°")))
                        {
                            p.Set(SanitizeGostString(val));
                        }
                    }
                }

                // 3. Fix ViewSheet parameters
                foreach (ElementId sheetId in selectedSheetIds)
                {
                    ViewSheet sheet = doc.GetElement(sheetId) as ViewSheet;
                    if (sheet == null) continue;

                    foreach (Parameter p in sheet.Parameters)
                    {
                        if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
                        string val = p.AsString();
                        if (!string.IsNullOrEmpty(val) && (val.Contains("«") || val.Contains("»") || val.Contains("°")))
                        {
                            p.Set(SanitizeGostString(val));
                        }
                    }
                }
            }
            catch { }
        }

        private string SanitizeFileName(string name)
        {
            // Replace invalid filename characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c.ToString(), "");
            }

            return name.Trim();
        }

        private string GetDefaultExportDirectory(Document doc)
        {
            try
            {
                if (!doc.IsModelInCloud && !string.IsNullOrEmpty(doc.PathName))
                {
                    string dir = Path.GetDirectoryName(doc.PathName);
                    if (Directory.Exists(dir))
                    {
                        return dir;
                    }
                }
            }
            catch
            {
                // Fallback to Documents
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
