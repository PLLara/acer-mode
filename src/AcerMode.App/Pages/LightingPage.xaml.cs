using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public partial class LightingPage : UserControl
{
    public LightingPage(LightingViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.SweepRequested += Keyboard.Sweep;
    }
}
