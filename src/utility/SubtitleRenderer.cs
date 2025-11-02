using System;
using System.Windows.Threading;

namespace Glossa.src.utility
{
    public static class SubtitleRenderer
    {
        private static SubtitlesWindow? _window;

        // Initialize once when your main app starts
        public static void Initialize(SubtitlesWindow window)
        {
            _window = window;
        }

        // Public method you can call from anywhere
        public static void ShowText(string text)
        {
            if (_window == null) return;

            // Use Dispatcher to ensure safe UI-thread updates
            _window.Dispatcher.Invoke(() =>
            {
                _window.UpdateSubtitle(text);
            });
        }

        // Optional: check visibility, toggle, etc.
        public static bool IsVisible => _window?.IsVisible ?? false;

        public static void SetVisible(bool show)
        {
            if (_window == null) return;
            if (show)
                _window.Show();
            else
                _window.Hide();
        }
    }
}
