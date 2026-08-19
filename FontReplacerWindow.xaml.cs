using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class FontReplacerWindow : Window
    {
        private readonly Document _doc;

        public FontReplacerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            LoadSystemFonts();
        }

        private void LoadSystemFonts()
        {
            InstalledFontCollection fonts = new InstalledFontCollection();
            List<string> fontNames = fonts.Families.Select(f => f.Name).OrderBy(n => n).ToList();
            FontsComboBox.ItemsSource = fontNames;

            string preferredFont = fontNames.FirstOrDefault(f => f.Equals("GOST Common", StringComparison.OrdinalIgnoreCase)
                                                              || f.Equals("ISOCPEUR", StringComparison.OrdinalIgnoreCase)
                                                              || f.Equals("Arial", StringComparison.OrdinalIgnoreCase));
            if (preferredFont != null) FontsComboBox.SelectedItem = preferredFont;
            else if (fontNames.Count > 0) FontsComboBox.SelectedIndex = 0;
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            string targetFont = FontsComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(targetFont))
            {
                MessageBox.Show("Пожалуйста, выберите шрифт для замены.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool applyDims = TargetDimensionsCheckBox.IsChecked ?? true;
            bool applyText = TargetTextNotesCheckBox.IsChecked ?? true;
            bool applyAnnot = TargetAnnotationsCheckBox.IsChecked ?? true;
            bool applySchedules = TargetSchedulesCheckBox.IsChecked ?? true;

            bool overrideSize = OverrideTextSizeCheckBox.IsChecked ?? false;
            double sizeMm = 2.5;
            if (overrideSize && double.TryParse(TextSizeTextBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedSize))
            {
                sizeMm = parsedSize;
            }

            bool overrideWidth = OverrideWidthScaleCheckBox.IsChecked ?? false;
            double widthScale = 1.0;
            if (overrideWidth && double.TryParse(WidthScaleTextBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedWidth))
            {
                widthScale = parsedWidth;
            }

            bool isBold = BoldCheckBox.IsChecked ?? false;
            bool isItalic = ItalicCheckBox.IsChecked ?? false;

            int updatedStyles = 0;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Замена шрифтов и стилей текста"))
            {
                t.Start();

                List<ElementType> styles = new List<ElementType>();
                if (applyText) styles.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<ElementType>());
                if (applyDims) styles.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(DimensionType)).Cast<ElementType>());
                if (applyAnnot) styles.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_GenericAnnotation).Cast<ElementType>());

                foreach (ElementType style in styles)
                {
                    bool changed = false;

                    // Font Name
                    Parameter pFont = style.get_Parameter(BuiltInParameter.TEXT_FONT);
                    if (pFont != null && !pFont.IsReadOnly)
                    {
                        pFont.Set(targetFont);
                        changed = true;
                    }

                    // Text Size
                    if (overrideSize)
                    {
                        Parameter pSize = style.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (pSize != null && !pSize.IsReadOnly)
                        {
                            pSize.Set(sizeMm / 304.8); // convert mm to feet
                            changed = true;
                        }
                    }

                    // Width Scale
                    if (overrideWidth)
                    {
                        Parameter pWidth = style.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE);
                        if (pWidth != null && !pWidth.IsReadOnly)
                        {
                            pWidth.Set(widthScale);
                            changed = true;
                        }
                    }

                    // Bold / Italic
                    Parameter pBold = style.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                    if (pBold != null && !pBold.IsReadOnly)
                    {
                        pBold.Set(isBold ? 1 : 0);
                        changed = true;
                    }

                    Parameter pItalic = style.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC);
                    if (pItalic != null && !pItalic.IsReadOnly)
                    {
                        pItalic.Set(isItalic ? 1 : 0);
                        changed = true;
                    }

                    if (changed) updatedStyles++;
                }

                t.Commit();
            }

            MessageBox.Show($"Шрифт '{targetFont}' и настройки стиля успешно применены к {updatedStyles} элементам.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
