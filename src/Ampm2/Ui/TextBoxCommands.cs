using System.Windows.Controls;
using System.Windows.Input;

namespace Ampm2.Ui;

/// <summary>
/// Commands for text boxes, bound once for every TextBox. <see cref="Clear"/> is what the × in a
/// ClearableField (Themes/Controls.xaml) runs: it empties the box (updating its binding) and keeps the focus there.
/// </summary>
public static class TextBoxCommands
{
    public static readonly RoutedUICommand Clear = new("Clear", nameof(Clear), typeof(TextBoxCommands));

    static TextBoxCommands()
    {
        CommandManager.RegisterClassCommandBinding(typeof(TextBox), new CommandBinding(Clear,
            (s, e) =>
            {
                if (s is not TextBox tb) return;
                tb.Clear();
                tb.Focus();
                e.Handled = true;
            },
            (s, e) => { e.CanExecute = s is TextBox { IsReadOnly: false, IsEnabled: true } tb && tb.Text.Length > 0; e.Handled = true; }));
    }

    /// <summary>Call once at startup (forces the static constructor, which registers the class binding).</summary>
    public static void Register() { }
}
