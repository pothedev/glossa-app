using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glossa.src.utility;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Glossa
{
    public partial class MainWindow : Window
    {
        private Settings _settings;
        private readonly HttpClient _httpClient = new HttpClient();
        private SubtitlesWindow? subtitlesWindow;
        public static Action<string, string, string> SafeAddMessage;

        private void SetRadioButtonFromSetting(Panel panel, string value)
        {
            if (panel == null || string.IsNullOrEmpty(value)) return;

            foreach (var child in panel.Children)
            {
                if (child is RadioButton radioButton &&
                    radioButton.Content.ToString().Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    radioButton.IsChecked = true;
                    break;
                }
            }
        }
        private void InitializeTTSSettings()
        {
            // Set Output TTS Model
            foreach (ComboBoxItem item in OutputTTSComboBox.Items)
            {
                if (item.Content.ToString() == _settings.OutputTTSModel)
                {
                    OutputTTSComboBox.SelectedItem = item;
                    break;
                }
            }

            // Set Input TTS Model
            foreach (ComboBoxItem item in InputTTSComboBox.Items)
            {
                if (item.Content.ToString() == _settings.InputTTSModel)
                {
                    InputTTSComboBox.SelectedItem = item;
                    break;
                }
            }

            // Inits
            // Voice genders
            SetRadioButtonFromSetting(UserVoiceGenderPanel, _settings.UserVoiceGender);
            SetRadioButtonFromSetting(TargetVoiceGenderPanel, _settings.TargetVoiceGender);

            // Translate toggles
            InitializeTranslateToggles();

            // Mode
            InitializeModeSettings();

            // Panel
            RefreshInfoPanel();

            //TranscriptPanel.LayoutUpdated += (_, _) =>
            //{
            //    TranscriptScroll.ScrollToEnd();
            //};

        }

        private void InputTTSComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputTTSComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selected = selectedItem.Content.ToString();
                _settings.InputTTSModel = selected;
                _settings.Save();
                RefreshInfoPanel();

                UpdateVoiceGenderAvailability(_settings.TargetLanguage, false);
            }
        }

        private void OutputTTSComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputTTSComboBox.SelectedItem is ComboBoxItem selectedItem)
            {   
                string selected = selectedItem.Content.ToString();
                _settings.OutputTTSModel = selected;
                _settings.Save();
                RefreshInfoPanel();

                UpdateVoiceGenderAvailability(_settings.UserLanguage, true);
            }
        }

        // Add these event handlers in your constructor:
        public MainWindow()
        {
            InitializeComponent();
            _settings = Settings.Load();

            SafeAddMessage = (sender, original, translated) =>
            {
                AddMessageInternal(sender, original, translated);
            };

            // Voice gender
            UserVoiceGenderPanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(InputVoiceGender_Checked));
            TargetVoiceGenderPanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(OutputVoiceGender_Checked));

            // Translate toggles
            YourTogglePanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(InputTranslate_Checked));
            TargetTogglePanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(OutputTranslate_Checked));

            // Mode radio panels
            UserModePanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(UserMode_Checked));
            TargetModePanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(TargetMode_Checked));


            Loaded += async (s, e) => await LoadLanguagesAsync();
            InitializeTTSSettings();
        }

        private void InputVoiceGender_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.UserVoiceGender = radioButton.Content.ToString();
                _settings.Save();
                RefreshInfoPanel();
            }
        }

        private void OutputVoiceGender_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.TargetVoiceGender = radioButton.Content.ToString();
                _settings.Save();
                RefreshInfoPanel();
            }
        }

        private async Task LoadLanguagesAsync()
        {
            try
            {

                string jsonPath = Path.Combine(Path.GetTempPath(), "languages.json");
                using (var resourceStream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("Glossa.data.languages.json")) // Namespace + file name
                using (var fileStream = File.Create(jsonPath))
                {
                    resourceStream.CopyTo(fileStream);
                }


                if (!File.Exists(jsonPath)) return;

                string json = await File.ReadAllTextAsync(jsonPath);
                var languages = JsonSerializer.Deserialize<List<LanguageItem>>(json);

                UserLanguageCombobox.Items.Clear();
                TargetLanguageCombobox.Items.Clear();

                // Pre-load all flag images
                var flagImages = new Dictionary<string, ImageSource>();
                foreach (var lang in languages)
                {
                    flagImages[lang.flag] = await LoadImageFromUrlAsync(lang.flag);
                }

                // Create items
                foreach (var lang in languages)
                {
                    UserLanguageCombobox.Items.Add(CreateLanguageItem(lang, flagImages[lang.flag]));
                    TargetLanguageCombobox.Items.Add(CreateLanguageItem(lang, flagImages[lang.flag]));
                }

                // Now that comboboxes are populated, initialize the selections
                InitializeLanguageSelections();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading languages: {ex.Message}");
            }
        }

        private void InitializeLanguageSelections()
        {
            System.Diagnostics.Debug.WriteLine($"Trying to set UserLanguage: {_settings.UserLanguage}");
            System.Diagnostics.Debug.WriteLine($"Trying to set TargetLanguage: {_settings.TargetLanguage}");

            bool userLangFound = false;
            bool targetLangFound = false;

            // Debug output for combobox contents
            //DebugComboBoxItems(UserLanguageCombobox, "UserLanguageCombobox");
            //DebugComboBoxItems(TargetLanguageCombobox, "TargetLanguageCombobox");

            foreach (ComboBoxItem item in UserLanguageCombobox.Items)
            {
                if (item.Tag is LanguageItem lang)
                {
                    System.Diagnostics.Debug.WriteLine($"Checking item with code: {lang.code}");
                    if (lang.code == _settings.UserLanguage)
                    {
                        UserLanguageCombobox.SelectedItem = item;
                        userLangFound = true;
                        System.Diagnostics.Debug.WriteLine($"Matched UserLanguage: {_settings.UserLanguage}");
                        break;
                    }
                }
            }

            foreach (ComboBoxItem item in TargetLanguageCombobox.Items)
            {
                if (item.Tag is LanguageItem lang)
                {
                    if (lang.code == _settings.TargetLanguage)
                    {
                        TargetLanguageCombobox.SelectedItem = item;
                        targetLangFound = true;
                        System.Diagnostics.Debug.WriteLine($"Matched TargetLanguage: {_settings.TargetLanguage}");
                        break;
                    }
                }
            }

            if (!userLangFound)
                System.Diagnostics.Debug.WriteLine($"Warning: UserLanguage {_settings.UserLanguage} not found in combobox items");

            if (!targetLangFound)
                System.Diagnostics.Debug.WriteLine($"Warning: TargetLanguage {_settings.TargetLanguage} not found in combobox items");
        }

        private ComboBoxItem CreateLanguageItem(LanguageItem lang, ImageSource flagImage)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new Image
            {
                Source = flagImage,
                Width = 16,
                Height = 10,
                Margin = new Thickness(0, 0, 8, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = lang.language,
                VerticalAlignment = VerticalAlignment.Center
            });

            return new ComboBoxItem
            {
                Content = panel,
                Tag = lang
            };
        }

        private async Task<BitmapImage> LoadImageFromUrlAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Important for cross-thread use
                    return bitmap;
                }
            }
            catch
            {
                // Return a default/empty image if loading fails
                return new BitmapImage();
            }
        }

        private void UserLanguageCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserLanguageCombobox.SelectedItem is ComboBoxItem selectedItem &&
                selectedItem.Tag is LanguageItem language)
            {
                // Use the selected language
                string selectedLanguage = language.language;
                string country = language.country;
                string code = language.code;

                _settings.UserLanguage = code;
                _settings.Save();
                RefreshInfoPanel();

                UpdateVoiceGenderAvailability(code, true);

            }
        }

        private void TargetLanguageCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetLanguageCombobox.SelectedItem is ComboBoxItem selectedItem &&
                selectedItem.Tag is LanguageItem language)
            {
                // Use the selected language
                string selectedLanguage = language.language;
                string country = language.country;
                string flagUrl = language.flag;
                string code = language.code;

                _settings.TargetLanguage = code;
                _settings.Save();
                RefreshInfoPanel();

                UpdateVoiceGenderAvailability(code, false);
                
            }
        }

        private void UpdateVoiceGenderAvailability(string languageCode, bool isUser) {
            bool maleAvailable = GoogleVoiceChecker.Google(languageCode)[0];
            bool femaleAvailable = GoogleVoiceChecker.Google(languageCode)[1];

            if (isUser)
            {
                if (_settings.OutputTTSModel != "Google Cloud")
                {
                    UserMaleRadio.Visibility = Visibility.Visible;
                    UserFemaleRadio.Visibility = Visibility.Visible;
                    return;
                }


                if (maleAvailable)
                {
                    UserMaleRadio.Visibility = Visibility.Visible;
                }
                else
                {
                    UserMaleRadio.Visibility = Visibility.Collapsed;
                }

                if (femaleAvailable)
                {
                    UserFemaleRadio.Visibility = Visibility.Visible;
                }
                else
                {
                    UserFemaleRadio.Visibility = Visibility.Collapsed;
                }
            }
            else {
                if (_settings.InputTTSModel != "Google Cloud")
                {
                    TargetMaleRadio.Visibility = Visibility.Visible;
                    TargetFemaleRadio.Visibility = Visibility.Visible;
                    return;
                }


                if (maleAvailable)
                {
                    TargetMaleRadio.Visibility = Visibility.Visible;
                }
                else
                {
                    TargetMaleRadio.Visibility = Visibility.Collapsed;
                }

                if (femaleAvailable)
                {
                    TargetFemaleRadio.Visibility = Visibility.Visible;
                }
                else
                {
                    TargetFemaleRadio.Visibility = Visibility.Collapsed;
                }
            }
        }


        //Mode
        private void UserMode_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.UserMode = radioButton.Content.ToString();
                _settings.Save();
                RefreshInfoPanel();
            }
        }

        private void TargetMode_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.TargetMode = radioButton.Content.ToString();
                _settings.Save();
                RefreshInfoPanel();
            }
        }

        private void InitializeModeSettings()
        {
            SetRadioButtonFromSetting(UserModePanel, _settings.UserMode);
            SetRadioButtonFromSetting(TargetModePanel, _settings.TargetMode);
        }




        //Translate toggles
        private void InputTranslate_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.InputTranslateEnabled = radioButton.Content.ToString() == "Enabled";
                _settings.Save();
                RefreshInfoPanel();
            }
        }

        private void OutputTranslate_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.OutputTranslateEnabled = radioButton.Content.ToString() == "Enabled";
                _settings.Save();
                RefreshInfoPanel();
            }
        }
        private void InitializeTranslateToggles()
        {
            SetRadioButtonFromSetting(YourTogglePanel, _settings.InputTranslateEnabled ? "Enabled" : "Disabled");
            SetRadioButtonFromSetting(TargetTogglePanel, _settings.OutputTranslateEnabled ? "Enabled" : "Disabled");
        }




        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Calculate available height: Window height - 170px (header) - 10px (top/bottom padding)
            double availableHeight = e.NewSize.Height - 170 - 10;
            ScrollableContentBorder.Height = availableHeight > 0 ? availableHeight : 0;
        }



        private void SubtitlesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (SubtitlesToggle.IsChecked == true)
            {
                // Show subtitles window
                if (subtitlesWindow == null)
                {
                    subtitlesWindow = new SubtitlesWindow
                    {
                        Owner = this
                    };

                    // 🔹 Position the window at bottom center
                    subtitlesWindow.WindowStartupLocation = WindowStartupLocation.Manual;

                    // Wait for window to initialize, then position it
                    subtitlesWindow.Loaded += (s, args) =>
                    {
                        double bottomMargin = 80;
                        subtitlesWindow.Left = (SystemParameters.WorkArea.Width - subtitlesWindow.ActualWidth) / 2;
                        subtitlesWindow.Top = SystemParameters.WorkArea.Height - subtitlesWindow.ActualHeight - bottomMargin;
                    };

                    // 🔹 Register this instance globally so it can be accessed anywhere
                    SubtitleRenderer.Initialize(subtitlesWindow);
                }

                subtitlesWindow.Show();
            }
            else
            {
                // Hide subtitles window
                subtitlesWindow?.Hide();
            }
        }



        private void SubtitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (subtitlesWindow != null && subtitlesWindow.IsVisible)
            {
                subtitlesWindow.SetTransparency(e.NewValue);
            }
        }



        private async void RefreshInfoPanel()
        {
            try
            {
                // Avatars
                string userGender = _settings.UserVoiceGender;
                string targetGender = _settings.TargetVoiceGender;

                UserAvatar.Source = new BitmapImage(new Uri(
                    $"pack://application:,,,/assets/avatar_{userGender.ToLower()}.png"));
                TargetAvatar.Source = new BitmapImage(new Uri(
                    $"pack://application:,,,/assets/avatar_{targetGender.ToLower()}.png"));

                // Flags
                string jsonPath = Path.Combine(Path.GetTempPath(), "languages.json");
                if (File.Exists(jsonPath))
                {
                    string json = await File.ReadAllTextAsync(jsonPath);
                    var languages = JsonSerializer.Deserialize<List<LanguageItem>>(json);
                    if (languages != null)
                    {
                        var userLang = languages.FirstOrDefault(l => l.code == _settings.UserLanguage);
                        var targetLang = languages.FirstOrDefault(l => l.code == _settings.TargetLanguage);
                        if (userLang != null)
                            UserFlag.Source = await LoadImageFromUrlAsync(userLang.flag);
                        if (targetLang != null)
                            TargetFlag.Source = await LoadImageFromUrlAsync(targetLang.flag);
                    }
                }

                // Text labels
                //UserLang.Text = _settings.UserLanguage;
                //TargetLang.Text = _settings.TargetLanguage;
                UserTTS.Text = _settings.InputTTSModel;
                TargetTTS.Text = _settings.OutputTTSModel;

                // Mode label
                string combinedMode = $"{_settings.UserMode} / {_settings.TargetMode}";
                ModeText.Text = combinedMode + " Mode";

                // Color coding
                if (_settings.UserMode == "Summary" || _settings.TargetMode == "Summary")
                    ModeLabel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E6BCE"));
                else
                    ModeLabel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("RefreshInfoPanel error: " + ex.Message);
            }
        }

        private void ScrollViewer_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.DeltaManipulation.Translation.Y);
            e.Handled = true;
        }

        private void AddMessageInternal(string sender, string original, string translated)
        {
            // We’re on UI thread now, safe to modify WPF controls
            var messageStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 7)
            };

            var senderLabel = new TextBlock
            {
                Text = sender + ":",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = (sender == "You")
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71D5FF"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE371"))
            };

            messageStack.Children.Add(senderLabel);

            var contentStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = (sender == "You") ? new Thickness(20, 0, 0, 0) : new Thickness(9, 0, 0, 0),
                MaxWidth = 186
            };

            contentStack.Children.Add(new TextBlock
            {
                Text = original,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            });

            contentStack.Children.Add(new TextBlock
            {
                Text = translated,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#595959")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            messageStack.Children.Add(contentStack);

            TranscriptMessages.Children.Add(messageStack);
            TranscriptScroll.ScrollToEnd();
        }
    }

}