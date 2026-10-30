using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Panels standing together under one strip of tabs, one of them drawn at a time, with a slot at the strip's right end for whatever a deck adds.
public abstract class TabDeck<TTab> : Grid where TTab : struct, Enum
{
    private const string RowLayout = "Auto,*";
    private const string StripColumnLayout = "*,Auto";
    private const int PanelRow = 1;
    private const int CornerColumn = 1;

    private readonly Panel _stripRow;
    private readonly Border _cornerSlot = new() { [Grid.ColumnProperty] = CornerColumn };
    private readonly Dictionary<TTab, ToggleButton> _tabButtons = new();
    private readonly Dictionary<TTab, Control> _panels = new();
    private TTab _selectedTab;

    // Builds the strip with a hidden tab for every tab the deck can offer, in the order given, standing at the foot of its row.
    protected TabDeck(IEnumerable<(TTab Tab, string Label)> tabs)
    {
        RowDefinitions = new RowDefinitions(RowLayout);

        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (tab, label) in tabs)
        {
            var button = new ToggleButton { Content = label, IsVisible = false };
            button.Classes.Add(ThemeClasses.PanelTab);
            button.Click += (_, _) => Select(tab);
            _tabButtons[tab] = button;
            strip.Children.Add(button);
        }

        var stripColumns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(StripColumnLayout),
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { strip, _cornerSlot },
        };

        _stripRow = new Panel { Children = { stripColumns } };
        Children.Add(_stripRow);
    }

    // Raised once a tab has been selected, by a press on it or from code.
    public event Action? TabSelected;

    // How tall the strip's row is, the tabs standing at its foot, or not a number where it is as tall as the tabs.
    public double StripHeight
    {
        get
        {
            return _stripRow.Height;
        }
        set
        {
            _stripRow.Height = value;
        }
    }

    // The tab the strip is showing, or the one it last showed where it holds no panel.
    public TTab SelectedTab
    {
        get
        {
            return _selectedTab;
        }
    }

    // The tabs whose panels are standing in the deck, in the order the strip gives them.
    public IEnumerable<TTab> HeldTabs
    {
        get
        {
            return _panels.Keys.OrderBy(tab => tab).ToList();
        }
    }

    // Whether no panel is standing in the deck.
    public bool IsEmpty
    {
        get
        {
            return _panels.Count == 0;
        }
    }

    // The strip's right end, which a deck with a control of its own to put there fills.
    protected Border CornerSlot
    {
        get
        {
            return _cornerSlot;
        }
    }

    // Whether a tab's panel is standing in the deck.
    public bool DoesHold(TTab tab)
    {
        return _panels.ContainsKey(tab);
    }

    // Keeps the strip's row reaching down to the top of a control standing beside the deck, so the panels' top edge is level with that control's.
    public void StandLevelWithTopOf(Control besideControl)
    {
        besideControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && besideControl.TranslatePoint(default, this) is { } besideTop)
            {
                StripHeight = besideTop.Y;
            }
        };
    }

    // Stands a panel in the deck, taking note of one already declared inside it in markup.
    public void Add(TTab tab, Control panel)
    {
        SetRow(panel, PanelRow);
        if (panel.Parent != this)
        {
            Children.Add(panel);
        }

        _panels[tab] = panel;
        ShowSelectedPanel();
    }

    // Takes a panel out of the deck, visible, and lays out the window it leaves before handing it back.
    public Control Remove(TTab tab)
    {
        var panel = _panels[tab];
        var window = TopLevel.GetTopLevel(this);

        _panels.Remove(tab);
        Children.Remove(panel);
        panel.IsVisible = true;

        ShowSelectedPanel();
        window?.UpdateLayout();
        return panel;
    }

    // Moves some of the panels standing here into another deck and shows a tab there.
    public void MoveInto(TabDeck<TTab> otherDeck, IEnumerable<TTab> tabs, TTab tabToShow)
    {
        foreach (var tab in tabs)
        {
            otherDeck.Add(tab, Remove(tab));
        }

        otherDeck.Select(tabToShow);
    }

    // Moves every panel standing here into another deck, which shows the tab this one was showing.
    public void MoveAllInto(TabDeck<TTab> otherDeck)
    {
        MoveInto(otherDeck, HeldTabs, SelectedTab);
    }

    // Shows one of the panels, remembering a tab the deck does not hold and showing the first one it does instead.
    public void Select(TTab tab)
    {
        _selectedTab = tab;
        ShowSelectedPanel();
        TabSelected?.Invoke();
    }

    // Puts the strip and the panels in step with what is held and what is selected, moving a selection whose panel has left to one still here.
    private void ShowSelectedPanel()
    {
        if (!DoesHold(_selectedTab) && !IsEmpty)
        {
            _selectedTab = HeldTabs.First();
        }

        foreach (var (tab, button) in _tabButtons)
        {
            button.IsVisible = DoesHold(tab);
            button.IsChecked = IsSelected(tab);
        }

        foreach (var (tab, panel) in _panels)
        {
            panel.IsVisible = IsSelected(tab);
        }
    }

    // Whether a tab is the one selected.
    private bool IsSelected(TTab tab)
    {
        return EqualityComparer<TTab>.Default.Equals(tab, _selectedTab);
    }
}
