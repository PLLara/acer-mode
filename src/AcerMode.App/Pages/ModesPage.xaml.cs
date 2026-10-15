using System.Windows.Controls;

namespace AcerMode.App.Pages;

public partial class ModesPage : UserControl, IAnimatedPage
{
    public ModesPage() => InitializeComponent();

    // Cartões entram em cascata.
    public void PlayEntrance() => PageAnimations.Stagger(CardList, 40);
}
