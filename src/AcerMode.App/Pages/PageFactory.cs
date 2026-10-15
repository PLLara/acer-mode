using System.Windows;
using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public static class PageFactory
{
    public static readonly string[] Keys = { "modos", "iluminacao", "sistema", "ventoinhas", "ajustes" };

    public static string Title(string key) => key switch
    {
        "modos" => "Modos",
        "iluminacao" => "Iluminação",
        "sistema" => "Sistema",
        "ventoinhas" => "Ventoinhas",
        "ajustes" => "Ajustes",
        _ => key,
    };

    public static FrameworkElement Create(string key, Vm vm) => key switch
    {
        "modos" => new ModesPage { DataContext = vm.Modes },
        _ => new TextBlock { Text = "Em construção", Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text2Brush") },
    };
}
