using Microsoft.UI.Xaml;

namespace Uniquer.Helpers;

public static class MainPageViewHelpers
{
    public static string GetWindowTitle(string? basePath) =>
        $"Uniquer - {(string.IsNullOrWhiteSpace(basePath) ? "<none>" : basePath)}";

    public static bool Not(bool value) => !value;

    public static Visibility NotVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility AnyVisibility(bool v1, bool v2) =>
        v1 || v2 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NoneVisibility(bool v1, bool v2) =>
        !v1 && !v2 ? Visibility.Visible : Visibility.Collapsed;
}
