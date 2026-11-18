using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Shared.Ui.Common.ValueConverters
{
    /// <summary>A path cell's text: the file name, or "(none)" for an empty path. The bin grid and SuperView's slot grid show paths this way.</summary>
    public sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string path && path.Length != 0 ? Path.GetFileName(path) : "(none)";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
