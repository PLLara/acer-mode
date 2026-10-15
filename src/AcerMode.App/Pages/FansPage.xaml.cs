using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public partial class FansPage : UserControl
{
    public FansPage(FansViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
