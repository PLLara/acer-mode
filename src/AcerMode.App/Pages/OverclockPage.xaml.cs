using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public partial class OverclockPage : UserControl
{
    public OverclockPage(OverclockViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        IsVisibleChanged += (_, _) => { if (IsVisible) vm.Refresh(); };
    }
}
