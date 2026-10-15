using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        IsVisibleChanged += (_, _) => { if (IsVisible) vm.RefreshLogCommand.Execute(null); };
    }
}
