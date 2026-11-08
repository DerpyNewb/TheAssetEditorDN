using System.Windows;
using AvalonDock.Themes;

namespace Editors.Shared.Core.Common.BaseControl.Docking
{
    /// <summary>VS2013 Dark's styles with every one of its brushes replaced by <see cref="Overrides"/>, whose colours follow the
    /// app's theme.</summary>
    public sealed class AssetEditorDockTheme : DictionaryTheme
    {
        public AssetEditorDockTheme() : base(Build()) { }

        public static ResourceDictionary Overrides() =>
            new() { Source = new Uri("/Editors.Shared.Core;component/Common/BaseControl/Docking/AssetEditorDockTheme.xaml", UriKind.Relative) };

        static ResourceDictionary Build()
        {
            var theme = new Vs2013DarkTheme().ThemeResourceDictionary;
            var overrides = Overrides();
            foreach (var key in overrides.Keys)
                theme[key] = overrides[key];
            return theme;
        }
    }
}
