using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace TemplateWithPreview;

internal static class CsvInputButtons
{
    public static void Populate(WrapPanel panel, IEnumerable<string> values, Action<string> append)
    {
        foreach (string value in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var button = new Button
            {
                Content = value,
                Margin = new System.Windows.Thickness(0, 0, 4, 3),
                Padding = new System.Windows.Thickness(7, 2, 7, 2),
                ToolTip = $"「{value}」を追加"
            };
            button.Click += (_, _) => append(value);
            panel.Children.Add(button);
        }
    }

    public static void Append(TextBox box, string value) => box.Text = Append(box.Text, value);

    public static void Append(ComboBox box, string value) => box.Text = Append(box.Text, value);

    private static string Append(string? current, string value)
    {
        var values = (current ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        return string.Join(", ", values);
    }
}
