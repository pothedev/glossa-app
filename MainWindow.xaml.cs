using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glossa
{
    public partial class MainWindow : Window
    {
        private Settings _settings;
        private readonly HttpClient _httpClient = new HttpClient();

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

            // Initialize voice gender radio buttons
            SetRadioButtonFromSetting(UserVoiceGenderPanel, _settings.UserVoiceGender);
            SetRadioButtonFromSetting(TargetVoiceGenderPanel, _settings.TargetVoiceGender);
        }

        private void InputTTSComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputTTSComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selected = selectedItem.Content.ToString();
                _settings.InputTTSModel = selected;
                _settings.Save();

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

                UpdateVoiceGenderAvailability(_settings.UserLanguage, true);
            }
        }

        // Add these event handlers in your constructor:
        public MainWindow()
        {
            InitializeComponent();
            _settings = Settings.Load();

            // Add these lines:
            UserVoiceGenderPanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(InputVoiceGender_Checked));
            TargetVoiceGenderPanel.AddHandler(RadioButton.CheckedEvent, new RoutedEventHandler(OutputVoiceGender_Checked));

            Loaded += async (s, e) => await LoadLanguagesAsync();
            InitializeTTSSettings();
        }

        private void InputVoiceGender_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.UserVoiceGender = radioButton.Content.ToString();
                _settings.Save();
            }
        }

        private void OutputVoiceGender_Checked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton radioButton && radioButton.IsChecked == true)
            {
                _settings.TargetVoiceGender = radioButton.Content.ToString();
                _settings.Save();
            }
        }

        private async Task LoadLanguagesAsync()
        {
            try
            {
                string jsonPath = "../../../languages.json";
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

                UpdateVoiceGenderAvailability(code, false);
                
            }
        }

        private void UpdateVoiceGenderAvailability(string languageCode, bool isUser) {
            bool maleAvailable = GoogleVoiceChecker.Google(languageCode)[0];
            bool femaleAvailable = GoogleVoiceChecker.Google(languageCode)[1];

            System.Diagnostics.Debug.WriteLine("hi");

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


        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Calculate available height: Window height - 170px (header) - 10px (top/bottom padding)
            double availableHeight = e.NewSize.Height - 170 - 10;
            ScrollableContentBorder.Height = availableHeight > 0 ? availableHeight : 0;
        }
    }
}