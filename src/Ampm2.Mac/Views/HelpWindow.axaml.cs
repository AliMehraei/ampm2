using System.Collections.Generic;
using System.Linq;
using Ampm2.Help;
using Avalonia.Controls;
using Avalonia.Input;

namespace Ampm2.Mac.Views;

public partial class HelpWindow : Window
{
    private readonly IReadOnlyList<HelpTopic> _all = HelpContent.Topics(HelpPlatform.Mac);

    public HelpWindow()
    {
        InitializeComponent();
        TopicList.ItemsSource = _all;
        TopicList.SelectionChanged += (_, _) =>
        {
            if (TopicList.SelectedItem is HelpTopic t) { Article.DataContext = t; ArticleScroll.ScrollToHome(); }
        };
        TopicList.SelectedIndex = 0;
        SearchBox.TextChanged += (_, _) => Filter();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = ""; else Close(); e.Handled = true; }
            else if (e.Key == Key.F && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0) { SearchBox.Focus(); e.Handled = true; }
        };
    }

    /// <summary>Shows a topic by id (e.g. "saved", "reboot"); unknown ids keep the current topic.</summary>
    public void ShowTopic(string? id)
    {
        if (id == null) return;
        SearchBox.Text = "";
        var t = _all.FirstOrDefault(x => x.Id == id);
        if (t != null) TopicList.SelectedItem = t;
    }

    private void Filter()
    {
        var shown = _all.Where(t => t.Matches(SearchBox.Text ?? "")).ToList();
        var keep = TopicList.SelectedItem as HelpTopic;
        TopicList.ItemsSource = shown;
        NoResults.IsVisible = shown.Count == 0;
        TopicList.SelectedItem = keep != null && shown.Contains(keep) ? keep : shown.FirstOrDefault();
    }
}
