using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AcerMode.App.Theme;

namespace AcerMode.App.Controls;

// Número que conta até o valor novo em vez de pular. Null mostra "—".
public sealed class AnimatedNumber : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(AnimatedNumber), new PropertyMetadata(null, (d, e) => ((AnimatedNumber)d).OnValue()));

    static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "Display", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0.0, (d, _) => ((AnimatedNumber)d).Render()));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(AnimatedNumber), new PropertyMetadata("{0:N0}", (d, _) => ((AnimatedNumber)d).Render()));

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    bool hasValue;

    public AnimatedNumber() => Text = "—";

    void OnValue()
    {
        if (Value is not { } v) { hasValue = false; BeginAnimation(DisplayProperty, null); Text = "—"; return; }
        if (!hasValue || !Motion.Enabled)
        {
            hasValue = true;
            BeginAnimation(DisplayProperty, null);
            SetValue(DisplayProperty, v);
            Render();
            return;
        }
        BeginAnimation(DisplayProperty, new DoubleAnimation(v, Motion.D(400)) { EasingFunction = Motion.EaseOut });
    }

    void Render()
    {
        if (!hasValue) return;
        Text = string.Format(CultureInfo.GetCultureInfo("pt-BR"), Format, Math.Round((double)GetValue(DisplayProperty)));
    }
}
