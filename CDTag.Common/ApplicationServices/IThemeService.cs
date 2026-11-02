namespace CDTag.Common.ApplicationServices
{
    /// <summary>Lets view models read and change the colour theme (implemented by CDTag.Views ThemeManager).</summary>
    public interface IThemeService
    {
        /// <summary>"System", "Light" or "Dark". Setting it applies and saves the theme.</summary>
        string ThemeName { get; set; }
    }
}
