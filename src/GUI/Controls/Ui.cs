using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Controls;

internal static class Ui
{
    internal static TextBlock Text(string text, double size = 13, IBrush color = null) =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = color ?? Palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };

    internal static TextBlock Message(string text) =>
        new()
        {
            Text = text,
            FontSize = 12.5,
            Foreground = Palette.Tertiary,
            Margin = new Thickness(8, 18),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

    internal static Button Button(string text, Action click, string tone = null)
    {
        Button button = new() { Content = text };
        if (tone is not null)
            button.Classes.Add(tone);
        button.Click += (_, _) => click();
        return button;
    }

    internal static Control Labeled(string label, Control content) =>
        new StackPanel { Spacing = 5, Children = { Text(label, 12, Palette.Secondary), content } };

    internal static TextBox Input(string placeholder)
    {
        TextBox input = new() { PlaceholderText = placeholder };
        input.TextChanged += (_, _) =>
        {
            if (input.Text is { } text && (text.Contains('\r') || text.Contains('\n')))
            {
                input.Text = text.Replace("\r", "").Replace("\n", "");
                input.CaretIndex = input.Text.Length;
            }
        };
        return input;
    }
}
