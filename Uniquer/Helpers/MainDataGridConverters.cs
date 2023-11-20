using Microsoft.UI.Xaml.Data;
using Uniquer.Models;

namespace Uniquer.Helpers;

public class MainDataGridSize1Converter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is not ImagesDifference imagesDifference ? null : $"{imagesDifference.Width1}x{imagesDifference.Height1}";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MainDataGridSize2Converter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is not ImagesDifference imagesDifference ? null : $"{imagesDifference.Width2}x{imagesDifference.Height2}";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MainDataGridFileNameConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is not string sValue ? null : Path.GetFileName(sValue);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}