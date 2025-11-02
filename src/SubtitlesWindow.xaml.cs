using Glossa.src.utility;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Glossa
{
    public partial class SubtitlesWindow : Window
    {
        // Resize grip thickness (in DIPs) we consider as “edge” for hit testing:
        private const int ResizeEdge = 6;
        private bool isFrozen = false;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;

        private readonly List<string> demoTexts = new()
        {
            "This is a short line.",
            "Now a bit longer line that should wrap when the width is narrow enough.",
            "Sometimes a line is so long that it will require two or even three rows to display properly without truncation.",
            "Back to a short one.",
            "Final example — this sentence is intentionally verbose and serves to test automatic height increase after width reduction."
        };

        private int currentIndex = 0;
        private readonly DispatcherTimer demoTimer = new();

        public SubtitlesWindow()
        {
            InitializeComponent();

            // Start the demo timer
            //demoTimer.Interval = TimeSpan.FromSeconds(5);
            //demoTimer.Tick += DemoTimer_Tick;
            //demoTimer.Start();

            // Set initial text
            //UpdateSubtitle(demoTexts[currentIndex]);

            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }   

        private void DemoTimer_Tick(object? sender, EventArgs e)
        {
            currentIndex = (currentIndex + 1) % demoTexts.Count;
            UpdateSubtitle(demoTexts[currentIndex]);
        }

        // --- PUBLIC API: call this whenever you want to show new text ---
        public void UpdateSubtitle(string text)
        {
            if (isFrozen)
                return;

            SubtitleText.Text = text;
            RecalculateHeight();
        }

        // Recalculate height whenever width changes (user resizes) 
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) // only reflow on width changes
                RecalculateHeight();
        }

        // Measure required height for current text at current width
        private void RecalculateHeight()
        {
            // Current usable content width = window client width - horizontal padding
            // RootBorder.Padding.Left/Right are in DIPs.
            double contentWidth = Math.Max(0,
                ActualWidth - (RootBorder.Padding.Left + RootBorder.Padding.Right));

            if (contentWidth <= 0) return;

            // Ask WPF layout how tall the TextBlock wants to be at this width
            SubtitleText.Measure(new Size(contentWidth, double.PositiveInfinity));
            double desiredContentHeight = SubtitleText.DesiredSize.Height;

            // Add vertical padding back to compute the window height
            double totalHeight = desiredContentHeight + (RootBorder.Padding.Top + RootBorder.Padding.Bottom);

            // Avoid tiny oscillations
            if (Math.Abs(Height - totalHeight) > 0.5)
                Height = totalHeight;
        }

        // Make only LEFT/RIGHT edges resizable; block TOP/BOTTOM (so height is app-controlled)

        private IntPtr WndProc_NoVerticalResize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            if (msg == WM_NCHITTEST)
            {
                // Convert lParam (screen coords) to window coords
                int x = (short)((long)lParam & 0xFFFF);
                int y = (short)(((long)lParam >> 16) & 0xFFFF);

                var pt = PointFromScreen(new Point(x, y));
                double w = ActualWidth;
                double h = ActualHeight;

                bool left = pt.X >= 0 && pt.X <= ResizeEdge;
                bool right = pt.X >= w - ResizeEdge && pt.X <= w;
                bool top = pt.Y >= 0 && pt.Y <= ResizeEdge;
                bool bottom = pt.Y >= h - ResizeEdge && pt.Y <= h;

                // Allow ONLY left/right resize; block top/bottom (and corners vertically)
                if (left) { handled = true; return (IntPtr)HTLEFT; }
                if (right) { handled = true; return (IntPtr)HTRIGHT; }

                // If the hit is top/bottom/corners, treat as client (no vertical resize)
                if (top || bottom) { handled = true; return (IntPtr)HTCLIENT; }
            }
            return IntPtr.Zero;
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WndProc_NoVerticalResize);

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int GWL_EXSTYLE = -20;

            int exStyle = (int)NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        internal static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        }
        

        public void SetTransparency(double value)
        {
            // Clamp value (0.2–1.0 recommended)
            if (value < 0.2) value = 0.2;
            if (value > 1.0) value = 1.0;

            // Convert to alpha (0–255)
            byte alpha = (byte)(value * 255);

            // Set window background only (semi-transparent black)
            Background = new SolidColorBrush(Color.FromArgb(alpha, 20, 20, 20));
        }


        public void ToggleFreeze(bool freeze)
        {
            isFrozen = freeze;
        }


        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            isFrozen = !isFrozen;

            // Swap icon
            var iconPath = isFrozen
                ? "pack://application:,,,/assets/locked.png"
                : "pack://application:,,,/assets/unlocked.png";

            //LockIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
            LockIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));

            // Optional: dim text while locked
            SubtitleText.Opacity = isFrozen ? 0.8 : 1.0;

            // Inform global renderer (if you’re using SubtitleRenderer)
            ToggleFreeze(isFrozen);
        }

    }
}
