using Microsoft.UI.Xaml.Controls;
using Uniquer.ViewModels;

namespace Uniquer.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = App.GetService<MainViewModel>();

    public MainPage()
    {
        InitializeComponent();
    }
}
