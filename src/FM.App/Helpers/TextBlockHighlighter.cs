using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FM.Core.Search;

namespace FM.App.Helpers;

/// <summary>
/// Attached behavior for TextBlock that highlights characters matching the search query.
/// Preserves diacritic-insensitive matching while styling matched characters with bold text and accent color.
/// </summary>
public static class TextBlockHighlighter
{
    private static readonly Brush HighlightBrush = CreateFrozenBrush(Color.FromRgb(0xD9, 0x26, 0x62)); // Antigravity Accent
    private static readonly Brush DefaultBrush = CreateFrozenBrush(Color.FromRgb(0x1F, 0x29, 0x37));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty FullTextProperty =
        DependencyProperty.RegisterAttached(
            "FullText",
            typeof(string),
            typeof(TextBlockHighlighter),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty HighlightQueryProperty =
        DependencyProperty.RegisterAttached(
            "HighlightQuery",
            typeof(string),
            typeof(TextBlockHighlighter),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetFullText(DependencyObject obj) => (string)obj.GetValue(FullTextProperty);
    public static void SetFullText(DependencyObject obj, string value) => obj.SetValue(FullTextProperty, value);

    public static string GetHighlightQuery(DependencyObject obj) => (string)obj.GetValue(HighlightQueryProperty);
    public static void SetHighlightQuery(DependencyObject obj, string value) => obj.SetValue(HighlightQueryProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        string full = GetFullText(tb);
        string query = GetHighlightQuery(tb);

        tb.Inlines.Clear();

        if (string.IsNullOrEmpty(full)) return;

        if (string.IsNullOrWhiteSpace(query))
        {
            tb.Inlines.Add(new Run(full) { Foreground = DefaultBrush });
            return;
        }

        string normFull = NameMatcher.Normalize(full);
        string normQuery = NameMatcher.Normalize(query.TrimStart('>').Trim());

        if (string.IsNullOrEmpty(normQuery))
        {
            tb.Inlines.Add(new Run(full) { Foreground = DefaultBrush });
            return;
        }

        // Identify matched char positions in full text
        var matched = new bool[full.Length];
        int qIdx = 0;
        for (int i = 0; i < full.Length && qIdx < normQuery.Length; i++)
        {
            char fc = normFull[i];
            char qc = normQuery[qIdx];
            if (fc == qc)
            {
                matched[i] = true;
                qIdx++;
            }
        }

        // Render Inlines grouping consecutive matched / unmatched characters
        int start = 0;
        while (start < full.Length)
        {
            bool isMatch = matched[start];
            int end = start + 1;
            while (end < full.Length && matched[end] == isMatch)
            {
                end++;
            }

            string slice = full.Substring(start, end - start);
            var run = new Run(slice);
            if (isMatch)
            {
                run.Foreground = HighlightBrush;
                run.FontWeight = FontWeights.Bold;
            }
            else
            {
                run.Foreground = DefaultBrush;
                run.FontWeight = FontWeights.SemiBold;
            }

            tb.Inlines.Add(run);
            start = end;
        }
    }
}
