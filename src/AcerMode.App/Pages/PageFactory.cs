using System.Windows;
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
        "iluminacao" => new LightingPage(vm.Lighting),
        "sistema" => new SystemPage(vm.System),
        "ventoinhas" => new FansPage(vm.Fans),
        "ajustes" => new SettingsPage(vm.Settings),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Página desconhecida."),
    };
}
