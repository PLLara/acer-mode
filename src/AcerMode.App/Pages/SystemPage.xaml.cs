using System.Windows.Controls;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Pages;

public partial class SystemPage : UserControl
{
    public SystemPage(SystemViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
