using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Uniquer.Helpers;

public static class MainPageViewHelpers
{
    public static bool Not(bool value) => !value;
    public static Visibility NotVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;
}
