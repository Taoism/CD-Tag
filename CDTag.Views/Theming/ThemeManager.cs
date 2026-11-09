using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Common.Settings;
using CDTag.Common.Wpf;
using CDTag.Model.Settings;
using Microsoft.Win32;

namespace CDTag.Views.Theming
{
    /// <summary>The user's theme choice.</summary>
    public enum ThemePreference
    {
        /// <summary>Follow the Windows "app mode" (Settings > Personalization > Colors).</summary>
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Switches the application between the light and dark palettes at runtime.
    /// </summary>
    /// <remarks>
    /// Application.Resources.MergedDictionaries[0] holds the palette (Themes/Palettes/*.xaml),
    /// followed by the control styles. Every themed brush is referenced with DynamicResource, so
    /// replacing the palette dictionary re-colours all open windows immediately. Window frames
    /// (title bars) are switched through DWM.
    /// </remarks>
    public sealed class ThemeManager : INotifyPropertyChanged, IThemeService
    {
        private const string SettingsFileName = "appearance.json";
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static readonly Uri LightPaletteUri = new Uri("pack://application:,,,/Themes/Palettes/Light.xaml", UriKind.Absolute);
        private static readonly Uri DarkPaletteUri = new Uri("pack://application:,,,/Themes/Palettes/Dark.xaml", UriKind.Absolute);

        private static readonly ThemeManager _instance = new ThemeManager();

        private Application _application;
        private ResourceDictionary _palette;
        private ThemePreference _preference = ThemePreference.System;
        private bool _isDark;
        private bool _initialized;

        private ThemeManager()
        {
            SetThemeCommand = new DelegateCommand<object>(p =>
            {
                ThemePreference value;
                if (p is ThemePreference)
                    Preference = (ThemePreference)p;
                else if (p != null && Enum.TryParse(p.ToString(), true, out value))
                    Preference = value;
            });
        }

        /// <summary>The application-wide instance.</summary>
        public static ThemeManager Instance
        {
            get { return _instance; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Raised after the active palette changed (light &lt;-&gt; dark).</summary>
        public event EventHandler ThemeChanged;

        /// <summary>Command for menus; the parameter is "System", "Light" or "Dark".</summary>
        public ICommand SetThemeCommand { get; private set; }

        /// <summary>IThemeService: the preference as "System", "Light" or "Dark".</summary>
        public string ThemeName
        {
            get { return _preference.ToString(); }
            set
            {
                ThemePreference parsed;
                if (Enum.TryParse(value, true, out parsed))
                    Preference = parsed;
            }
        }

        /// <summary>Gets whether the dark palette is currently active.</summary>
        public bool IsDarkActive
        {
            get { return _isDark; }
        }

        public bool IsSystem { get { return _preference == ThemePreference.System; } }
        public bool IsLight { get { return _preference == ThemePreference.Light; } }
        public bool IsDark { get { return _preference == ThemePreference.Dark; } }

        /// <summary>Gets or sets the user's choice; setting it applies and saves it.</summary>
        public ThemePreference Preference
        {
            get { return _preference; }
            set
            {
                if (_preference == value)
                    return;

                _preference = value;
                OnPropertyChanged("Preference");
                OnPropertyChanged("ThemeName");
                OnPropertyChanged("IsSystem");
                OnPropertyChanged("IsLight");
                OnPropertyChanged("IsDark");

                Apply();
                Save();
            }
        }

        /// <summary>
        /// Loads the saved
        /// preference and starts following Windows theme changes. Call once at startup, after the
        /// IoC container is configured (settings use IPathService).
        /// </summary>
        public void Initialize(Application application)
        {
            if (application == null)
                throw new ArgumentNullException("application");
            if (_initialized)
                return;
            _initialized = true;

            _application = application;

            _preference = Load();
            CDTag.Common.ThemedMessageBox.IsDark = () => _isDark;

            // Dark title bars for every window as soon as it gets an HWND / when it loads.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            application.Exit += delegate { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; };

            Apply(force: true);
        }

        /// <summary>Applies the current frame colour to <paramref name="window"/> (call from OnSourceInitialized).</summary>
        public void ApplyToWindow(Window window)
        {
            if (window != null)
                GlassHelper.SetDarkTitleBar(window, _isDark);
        }

        /// <summary>Reads the Windows "Choose your app mode" setting.</summary>
        public static bool IsSystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    object value = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void Apply(bool force = false)
        {
            bool dark;
            switch (_preference)
            {
                case ThemePreference.Dark:
                    dark = true;
                    break;
                case ThemePreference.Light:
                    dark = false;
                    break;
                default:
                    dark = !SystemParameters.HighContrast && IsSystemDark();
                    break;
            }

            if (!force && dark == _isDark)
                return;

            _isDark = dark;

            if (_application != null)
            {
                var palette = new ResourceDictionary { Source = dark ? DarkPaletteUri : LightPaletteUri };
                var merged = _application.Resources.MergedDictionaries;
                int index = _palette != null ? merged.IndexOf(_palette) : -1;
                if (index >= 0)
                    merged[index] = palette;
                else
                    merged.Insert(0, palette);
                _palette = palette;
            }

            Application application = Application.Current;
            if (application != null)
            {
                foreach (Window window in application.Windows)
                    ApplyToWindow(window);
            }

            OnPropertyChanged("IsDarkActive");
            EventHandler handler = ThemeChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            ApplyToWindow(sender as Window);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
                return;

            Application application = Application.Current;
            if (application == null)
                return;

            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_preference == ThemePreference.System)
                    Apply();
            }));
        }

        private static ThemePreference Load()
        {
            AppearanceSettings settings;
            ThemePreference preference;
            if (SettingsFile.TryLoad(SettingsFileName, out settings) && settings != null &&
                Enum.TryParse(settings.Theme, true, out preference))
            {
                return preference;
            }
            return ThemePreference.System;
        }

        private void Save()
        {
            try
            {
                SettingsFile.Save(SettingsFileName, new AppearanceSettings { Theme = _preference.ToString() });
            }
            catch (Exception)
            {
                // Not being able to persist the choice must not break the UI.
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
