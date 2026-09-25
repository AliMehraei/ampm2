using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ampm2.Help;
using Ampm2.Ui;

namespace Ampm2;

public partial class HelpWindow : Window
{
    private readonly System.Collections.Generic.IReadOnlyList<HelpTopic> _all = HelpContent.Topics(HelpPlatform.Windows);

    public HelpWindow()
    {
        InitializeComponent();
        Icon = Logo.Image();
        HelpLogo.Source = Logo.Image();
        SourceInitialized += (_, _) => ThemeManager.ApplyChrome(this);
        TopicList.ItemsSource = _all;
        TopicList.SelectedIndex = 0;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (SearchBox.Text.Length > 0) SearchBox.Clear(); else Close(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); e.Handled = true; }
        };
    }

    /// <summary>Shows a topic by id (e.g. "saved", "admin"); unknown ids keep the current topic.</summary>
    public void ShowTopic(string? id)
    {
        if (id == null) return;
        SearchBox.Clear();
        var t = _all.FirstOrDefault(x => x.Id == id);
        if (t != null) TopicList.SelectedItem = t;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var shown = _all.Where(t => t.Matches(SearchBox.Text)).ToList();
        var keep = TopicList.SelectedItem as HelpTopic;
        TopicList.ItemsSource = shown;
        NoResults.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TopicList.SelectedItem = keep != null && shown.Contains(keep) ? keep : shown.FirstOrDefault();
    }

    private void Topic_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is HelpTopic t)
        {
            Article.DataContext = t;
            ArticleScroll.ScrollToTop();
        }
    }
}
