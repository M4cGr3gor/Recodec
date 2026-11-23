using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Recodec.Help;

namespace Recodec;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        var view = new ListCollectionView(HelpContent.Topics.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(HelpTopic.Group)));
        TopicList.ItemsSource = view;
    }

    public void ShowTopic(string id)
    {
        var topic = HelpContent.Get(id);
        TopicList.SelectedItem = topic;
        TopicList.ScrollIntoView(topic);
    }

    private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is HelpTopic t) Viewer.Document = Render(t.Body);
    }

    // ------------------------------------------------------------------ mini-formato -> FlowDocument
    private FlowDocument Render(string body)
    {
        var fg = (Brush?)TryFindResource("TextFillColorPrimaryBrush") ?? SystemColors.ControlTextBrush;
        var muted = (Brush?)TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray;
        var accent = (Brush?)TryFindResource("AccentTextFillColorPrimaryBrush") ?? Brushes.SteelBlue;
        var tipBg = (Brush?)TryFindResource("SystemFillColorAttentionBackgroundBrush") ?? Brushes.AliceBlue;
        var line = (Brush?)TryFindResource("CardStrokeColorDefaultBrush") ?? Brushes.LightGray;

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Foreground = fg,
            PagePadding = new Thickness(16, 8, 16, 16),
            LineHeight = 22,
            TextAlignment = TextAlignment.Left,
        };

        var lines = body.Replace("\r", "").Split('\n');
        List? list = null;
        Table? table = null;
        TableRowGroup? rows = null;
        var firstRow = true;

        void CloseBlocks()
        {
            list = null;
            table = null;
            rows = null;
        }

        for (var li = 0; li < lines.Length; li++)
        {
            var l = lines[li].TrimEnd();
            if (l.Length == 0) { CloseBlocks(); continue; }

            if (l.StartsWith("# "))
            {
                CloseBlocks();
                doc.Blocks.Add(new Paragraph(new Run(l[2..])) { FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            }
            else if (l.StartsWith("## "))
            {
                CloseBlocks();
                doc.Blocks.Add(new Paragraph(new Run(l[3..])) { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6), Foreground = accent });
            }
            else if (l.StartsWith("- "))
            {
                if (list is null)
                {
                    table = null;
                    list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(22, 0, 0, 0) };
                    doc.Blocks.Add(list);
                }
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                AddInlines(p.Inlines, l[2..]);
                list.ListItems.Add(new ListItem(p));
            }
            else if (l.StartsWith("|"))
            {
                var cells = l.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (table is null)
                {
                    list = null;
                    table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 12), BorderBrush = line, BorderThickness = new Thickness(1) };
                    // Ancho de cada columna proporcional a la longitud media de su texto (con un mínimo).
                    var tableRows = lines.Skip(li).TakeWhile(x => x.TrimStart().StartsWith('|'))
                        .Select(x => x.Trim().Trim('|').Split('|').Select(c => c.Replace("**", "").Trim().Length).ToArray()).ToList();
                    for (var i = 0; i < cells.Length; i++)
                    {
                        var avg = tableRows.Where(r => r.Length > i).Select(r => (double)r[i]).DefaultIfEmpty(10).Average();
                        table.Columns.Add(new TableColumn { Width = new GridLength(Math.Sqrt(Math.Clamp(avg, 6, 80)), GridUnitType.Star) });
                    }
                    rows = new TableRowGroup();
                    table.RowGroups.Add(rows);
                    doc.Blocks.Add(table);
                    firstRow = true;
                }
                var row = new TableRow();
                foreach (var c in cells)
                {
                    var p = new Paragraph { Margin = new Thickness(0), FontSize = 13 };
                    AddInlines(p.Inlines, c);
                    if (firstRow) { p.FontWeight = FontWeights.SemiBold; p.Foreground = muted; }
                    row.Cells.Add(new TableCell(p) { Padding = new Thickness(8, 5, 8, 5), BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1) });
                }
                rows!.Rows.Add(row);
                firstRow = false;
            }
            else if (l.StartsWith("> "))
            {
                CloseBlocks();
                var p = new Paragraph { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(12, 8, 12, 8), Background = tipBg, BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0) };
                AddInlines(p.Inlines, l[2..]);
                doc.Blocks.Add(p);
            }
            else
            {
                CloseBlocks();
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                AddInlines(p.Inlines, l);
                doc.Blocks.Add(p);
            }
        }
        return doc;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var parts = Regex.Split(text, @"(\*\*[^*]+\*\*)");
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                inlines.Add(new Bold(new Run(part[2..^2])));
            else
                inlines.Add(new Run(part));
        }
    }
}
