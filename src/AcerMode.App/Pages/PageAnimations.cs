using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcerMode.App.Theme;

namespace AcerMode.App.Pages;

public interface IAnimatedPage
{
    void PlayEntrance();
}

public static class PageAnimations
{
    // Filhos de um ItemsControl entram um depois do outro (opacidade + subida de 8 px).
    public static void Stagger(ItemsControl items, int stepMs)
    {
        if (!Motion.Enabled) return;
        for (int i = 0; i < items.Items.Count; i++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(i) is not UIElement el) continue;
            Rise(el, i * stepMs);
        }
    }

    public static void Rise(UIElement el, int delayMs, double dy = 8)
    {
        if (!Motion.Enabled) return;
        var t = new TranslateTransform(0, dy);
        el.RenderTransform = t;
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty, Motion.FromTo(0, 1, 220, delayMs: delayMs));
        t.BeginAnimation(TranslateTransform.YProperty, Motion.FromTo(dy, 0, 260, delayMs: delayMs));
    }
}
