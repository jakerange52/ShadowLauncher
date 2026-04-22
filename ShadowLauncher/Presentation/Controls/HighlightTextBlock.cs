using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ShadowLauncher.Presentation.Controls;

/// <summary>
/// A TextBlock that highlights occurrences of a search term inside its text.
/// Bind Text and Query — matched segments get a distinct background.
/// </summary>
public class HighlightTextBlock : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.Register(nameof(Query), typeof(string), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(nameof(HighlightBrush), typeof(Brush), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(Brushes.Gold, OnPropertyChanged));

    public static readonly DependencyProperty HighlightForegroundProperty =
        DependencyProperty.Register(nameof(HighlightForeground), typeof(Brush), typeof(HighlightTextBlock),
            new FrameworkPropertyMetadata(Brushes.Black, OnPropertyChanged));

    private readonly TextBlock _inner = new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public HighlightTextBlock()
    {
        AddVisualChild(_inner);
        AddLogicalChild(_inner);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public Brush HighlightForeground
    {
        get => (Brush)GetValue(HighlightForegroundProperty);
        set => SetValue(HighlightForegroundProperty, value);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _inner;

    protected override Size MeasureOverride(Size availableSize)
    {
        _inner.Measure(availableSize);
        return _inner.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _inner.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HighlightTextBlock)d).Rebuild();

    private void Rebuild()
    {
        _inner.Inlines.Clear();
        var text  = Text  ?? string.Empty;
        var query = Query ?? string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            _inner.Inlines.Add(new Run(text) { Foreground = Foreground });
            return;
        }

        int pos = 0;
        while (pos < text.Length)
        {
            int idx = text.IndexOf(query, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                _inner.Inlines.Add(new Run(text[pos..]) { Foreground = Foreground });
                break;
            }
            if (idx > pos)
                _inner.Inlines.Add(new Run(text[pos..idx]) { Foreground = Foreground });

            _inner.Inlines.Add(new Run(text.Substring(idx, query.Length))
            {
                Background = HighlightBrush,
                Foreground = HighlightForeground,
            });
            pos = idx + query.Length;
        }
    }
}
