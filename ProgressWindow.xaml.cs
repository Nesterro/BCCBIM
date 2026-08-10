using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BCCPlugIn
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
            SetWindowIcon();
        }

        private void SetWindowIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("BCCPlugIn.Resources.pdf_export_icon.png"))
                {
                    if (stream != null)
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch
            {
                // Ignore icon loading failures
            }
        }

        public void SetHeaderTitle(string title)
        {
            if (!string.IsNullOrEmpty(title))
            {
                HeaderTitleText.Text = title.StartsWith(" ") ? title : " | " + title;
            }
        }

        public void UpdateProgress(string status, double value, bool isIndeterminate = false)
        {
            StatusText.Text = status;
            ExportProgressBar.IsIndeterminate = isIndeterminate;
            if (!isIndeterminate)
            {
                ExportProgressBar.Value = value;
            }
            
            // Allow UI to process paint messages and redraw
            AllowUIToUpdate();
        }

        private void AllowUIToUpdate()
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(ExitFrame), frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private object ExitFrame(object f)
        {
            ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
            return null;
        }
    }
}
