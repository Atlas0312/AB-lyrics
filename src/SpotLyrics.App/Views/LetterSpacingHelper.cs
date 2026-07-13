using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpotLyrics.App.Views;

internal static class LetterSpacingHelper
{
    public static readonly DependencyProperty LetterSpacingProperty =
        DependencyProperty.RegisterAttached(
            "LetterSpacing",
            typeof(double),
            typeof(LetterSpacingHelper),
            new PropertyMetadata(0.0, OnLetterSpacingChanged));

    public static void SetLetterSpacing(TextBlock element, double value) =>
        element.SetValue(LetterSpacingProperty, value);

    public static double GetLetterSpacing(TextBlock element) =>
        (double)element.GetValue(LetterSpacingProperty);

    private static void OnLetterSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            Apply(textBlock, (double)e.NewValue);
        }
    }

    private static void Apply(TextBlock textBlock, double spacingPx)
    {
        var text = textBlock.Text ?? string.Empty;
        if (Math.Abs(spacingPx) < 0.01 || text.Length <= 1)
        {
            textBlock.TextEffects = null;
            return;
        }

        var effects = new TextEffectCollection();
        for (var i = 1; i < text.Length; i++)
        {
            effects.Add(new TextEffect
            {
                PositionStart = i,
                PositionCount = 1,
                Transform = new TranslateTransform(spacingPx * i, 0),
            });
        }

        textBlock.TextEffects = effects;
    }
}
