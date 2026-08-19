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
        private List<ElementType> _textStyles;

        public FontReplacerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadSystemFonts();
            LoadTextStyles();
        }

        private void LoadSystemFonts()
        {
            InstalledFontCollection fonts = new InstalledFontCollection();
            List<string> fontNames = fonts.Families.Select(f => f.Name).OrderBy(n => n).ToList();
            FontsComboBox.ItemsSource = fontNames;

            string preferredFont = fontNames.FirstOrDefault(f => f.Equals("GOST Common", StringComparison.OrdinalIgnoreCase) || f.Equals("ISOCPEUR", StringComparison.OrdinalIgnoreCase) || f.Equals("Arial", StringComparison.OrdinalIgnoreCase));
            if (preferredFont != null) FontsComboBox.SelectedItem = preferredFont;
            else if (fontNames.Count > 0) FontsComboBox.SelectedIndex = 0;
        }

        private void LoadTextStyles()
        {
            _textStyles = new List<ElementType>();

            // TextNoteTypes
            var textTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<ElementType>();

            // DimensionTypes
            var dimTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<ElementType>();

            _textStyles.AddRange(textTypes);
            _textStyles.AddRange(dimTypes);

            StylesListBox.ItemsSource = _textStyles.Select(t => $"{t.Name} ({t.Category?.Name ?? "Стиль"})").ToList();
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            string targetFont = FontsComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(targetFont))
            {
                MessageBox.Show("Пожалуйста, выберите шрифт для замены.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Замена шрифтов"))
            {
                t.Start();
                foreach (ElementType style in _textStyles)
                {
                    Parameter pFont = style.get_Parameter(BuiltInParameter.TEXT_FONT);
                    if (pFont != null && !pFont.IsReadOnly)
                    {
                        pFont.Set(targetFont);
                        count++;
                    }
                }
                t.Commit();
            }

            MessageBox.Show($"Шрифт '{targetFont}' успешно применен к {count} стилям аннотаций.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
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
