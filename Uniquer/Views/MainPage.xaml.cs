using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml.Controls;
using Uniquer.Services;
using Uniquer.ViewModels;

namespace Uniquer.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = App.GetService<MainViewModel>();

    public MainPage()
    {
        InitializeComponent();
        //ViewModel.BasePath = @"D:\temp\img-tst"; 
        ViewModel.BasePath = @"E:\wallpapers\";
    }
}
