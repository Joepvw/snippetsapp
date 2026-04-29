using System.Windows;
using System.Windows.Controls;
using SnippetLauncher.App.Services;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.App.Views;

public partial class PlaceholderFillDialog : Window
{
    private readonly List<(string Name, TextBox Box)> _fields = [];
    private readonly PlaceholderFillContext? _context;
    private TextBox? _lastFocused;

    public Dictionary<string, string>? Result { get; private set; }

    /// <summary>The TextBox most recently focused, or the first field if none yet.</summary>
    public TextBox? ActiveField => _lastFocused ?? (_fields.Count > 0 ? _fields[0].Box : null);

    public PlaceholderFillDialog(string snippetTitle, IReadOnlyList<Placeholder> placeholders)
        : this(snippetTitle, placeholders, null) { }

    public PlaceholderFillDialog(string snippetTitle, IReadOnlyList<Placeholder> placeholders, PlaceholderFillContext? context)
    {
        InitializeComponent();

        TitleBlock.Text = $"Snippet: {snippetTitle}";
        _context = context;

        foreach (var ph in placeholders)
        {
            var label = new Label { Content = ph.Label };
            var box = new TextBox { Text = ph.Default };
            box.GotFocus += (_, _) => _lastFocused = box;
            FieldsPanel.Items.Add(label);
            FieldsPanel.Items.Add(box);
            _fields.Add((ph.Name, box));
        }

        Loaded += (_, _) =>
        {
            _context?.Push(this);
            if (_fields.Count > 0)
            {
                _fields[0].Box.Focus();
                _fields[0].Box.SelectAll();
            }
        };

        Closed += (_, _) => _context?.Remove(this);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        Result = _fields.ToDictionary(f => f.Name, f => f.Box.Text);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
