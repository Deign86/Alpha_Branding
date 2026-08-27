using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Alpha.Branding.Services;

/// <summary>
/// Converts GitHub release markdown into rich, native WPF visual elements with Dark/Gold styling.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex InlinePattern = new(
        @"(?<bold>\*\*(.*?)\*\*|__(.*?)__)|(?<italic>(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)(.*?)(?<!_)_(?!_))|(?<code>`([^`]+)`)|(?<link>\[([^\]]+)\]\(([^)]+)\))",
        RegexOptions.Compiled);

    public static void RenderTo(string markdown, StackPanel targetContainer, ResourceDictionary? resources = null)
    {
        targetContainer.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown)) return;

        var textPrimaryBrush = (Brush?)(resources?["TextPrimary"] ?? Application.Current?.TryFindResource("TextPrimary"))
                               ?? new SolidColorBrush(Color.FromRgb(240, 240, 240));
        var goldBrush = (Brush?)(resources?["Gold"] ?? Application.Current?.TryFindResource("Gold"))
                        ?? new SolidColorBrush(Color.FromRgb(197, 160, 89));
        var goldLightBrush = (Brush?)(resources?["GoldLight"] ?? Application.Current?.TryFindResource("GoldLight"))
                             ?? new SolidColorBrush(Color.FromRgb(226, 194, 133));

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                targetContainer.Children.Add(new Border { Height = 6 });
                continue;
            }

            var trimmedStart = line.TrimStart();

            // 1. Headers
            if (trimmedStart.StartsWith("#### "))
            {
                var headerText = trimmedStart[5..].Trim();
                var tb = CreateTextBlock(headerText, fontSize: 12, fontWeight: FontWeights.Bold, foreground: goldBrush, margin: new Thickness(0, 8, 0, 4), textPrimaryBrush, goldLightBrush);
                targetContainer.Children.Add(tb);
                continue;
            }
            if (trimmedStart.StartsWith("### "))
            {
                var headerText = trimmedStart[4..].Trim();
                var tb = CreateTextBlock(headerText, fontSize: 13, fontWeight: FontWeights.Bold, foreground: goldBrush, margin: new Thickness(0, 10, 0, 4), textPrimaryBrush, goldLightBrush);
                targetContainer.Children.Add(tb);
                continue;
            }
            if (trimmedStart.StartsWith("## "))
            {
                var headerText = trimmedStart[3..].Trim();
                var tb = CreateTextBlock(headerText, fontSize: 14, fontWeight: FontWeights.Bold, foreground: goldLightBrush, margin: new Thickness(0, 12, 0, 5), textPrimaryBrush, goldLightBrush);
                targetContainer.Children.Add(tb);
                continue;
            }
            if (trimmedStart.StartsWith("# "))
            {
                var headerText = trimmedStart[2..].Trim();
                var tb = CreateTextBlock(headerText, fontSize: 15, fontWeight: FontWeights.Bold, foreground: textPrimaryBrush, margin: new Thickness(0, 14, 0, 6), textPrimaryBrush, goldLightBrush);
                targetContainer.Children.Add(tb);
                continue;
            }

            // 2. Bullet list items
            if (trimmedStart.StartsWith("- ") || trimmedStart.StartsWith("* ") || trimmedStart.StartsWith("+ "))
            {
                var itemText = trimmedStart[2..].Trim();

                var grid = new Grid
                {
                    Margin = new Thickness(0, 2, 0, 3)
                };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = "•",
                    Foreground = goldLightBrush,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(4, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(bullet, 0);
                grid.Children.Add(bullet);

                var contentTb = CreateTextBlock(itemText, fontSize: 12, fontWeight: FontWeights.Normal, foreground: textPrimaryBrush, margin: new Thickness(0), textPrimaryBrush, goldLightBrush);
                contentTb.LineHeight = 18;
                Grid.SetColumn(contentTb, 1);
                grid.Children.Add(contentTb);

                targetContainer.Children.Add(grid);
                continue;
            }

            // 3. Regular paragraph lines
            var pTb = CreateTextBlock(trimmedStart, fontSize: 12, fontWeight: FontWeights.Normal, foreground: textPrimaryBrush, margin: new Thickness(0, 2, 0, 4), textPrimaryBrush, goldLightBrush);
            pTb.LineHeight = 18;
            targetContainer.Children.Add(pTb);
        }
    }

    public static TextBlock CreateTextBlock(string text, double fontSize, FontWeight fontWeight, Brush foreground, Thickness margin, Brush defaultForeground, Brush accentForeground)
    {
        var tb = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin
        };

        PopulateInlines(tb.Inlines, text, defaultForeground, accentForeground);
        return tb;
    }

    public static void PopulateInlines(InlineCollection inlines, string text, Brush defaultForeground, Brush accentForeground)
    {
        if (string.IsNullOrEmpty(text)) return;

        var lastIndex = 0;
        var matches = InlinePattern.Matches(text);

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var plainText = text.Substring(lastIndex, match.Index - lastIndex);
                inlines.Add(new Run(plainText));
            }

            if (match.Groups["bold"].Success)
            {
                var boldContent = !string.IsNullOrEmpty(match.Groups[2].Value) ? match.Groups[2].Value : match.Groups[3].Value;
                var bold = new Bold();
                PopulateInlines(bold.Inlines, boldContent, defaultForeground, accentForeground);
                inlines.Add(bold);
            }
            else if (match.Groups["italic"].Success)
            {
                var italicContent = !string.IsNullOrEmpty(match.Groups[5].Value) ? match.Groups[5].Value : match.Groups[6].Value;
                var italic = new Italic();
                PopulateInlines(italic.Inlines, italicContent, defaultForeground, accentForeground);
                inlines.Add(italic);
            }
            else if (match.Groups["code"].Success)
            {
                var codeContent = match.Groups[8].Value;
                var codeRun = new Run(codeContent)
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Foreground = accentForeground
                };
                inlines.Add(codeRun);
            }
            else if (match.Groups["link"].Success)
            {
                var linkText = match.Groups[10].Value;
                var linkUrl = match.Groups[11].Value;

                var link = new Hyperlink(new Run(linkText))
                {
                    NavigateUri = Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri) ? uri : null,
                    Foreground = accentForeground
                };
                link.RequestNavigate += (s, e) =>
                {
                    try
                    {
                        if (e.Uri != null)
                        {
                            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        }
                    }
                    catch { }
                    e.Handled = true;
                };
                inlines.Add(link);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            var remaining = text.Substring(lastIndex);
            inlines.Add(new Run(remaining));
        }
    }
}
