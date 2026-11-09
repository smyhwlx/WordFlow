using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WordFlow.App.Views.Controls;

/// <summary>
/// A TextBlock that renders the typed prefix of <see cref="Text"/> in bold accent color,
/// so users can see at a glance why each suggestion matched.
/// </summary>
public sealed class PrefixHighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight),
        typeof(string),
        typeof(PrefixHighlightTextBlock),
        new FrameworkPropertyMetadata(string.Empty, static (dependencyObject, _) =>
        {
            if (dependencyObject is PrefixHighlightTextBlock block) block.RebuildInlines();
        }));

    private bool rebuilding;

    public string Highlight
    {
        get => (string)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    static PrefixHighlightTextBlock()
    {
        TextProperty.OverrideMetadata(
            typeof(PrefixHighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, static (dependencyObject, _) =>
            {
                if (dependencyObject is PrefixHighlightTextBlock block) block.RebuildInlines();
            }));
    }

    private void RebuildInlines()
    {
        if (rebuilding) return;
        rebuilding = true;
        try
        {
            string text = Text ?? string.Empty;
            string prefix = (Highlight ?? string.Empty).Trim();
            Inlines.Clear();
            if (text.Length == 0) return;
            if (prefix.Length > 0 && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length <= text.Length)
            {
                var head = new Run(text[..prefix.Length]) { FontWeight = FontWeights.Bold };
                head.SetResourceReference(TextElement.ForegroundProperty, "Brush.AccentStrong");
                var tail = new Run(text[prefix.Length..]);
                Inlines.Add(head);
                if (tail.Text.Length > 0) Inlines.Add(tail);
            }
            else
            {
                Inlines.Add(new Run(text));
            }
        }
        finally
        {
            rebuilding = false;
        }
    }
}
