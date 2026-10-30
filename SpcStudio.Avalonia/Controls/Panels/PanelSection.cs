using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A folding section of a panel: a framed container, a banner across the top of it that is pressed, and a body that follows the banner. The banner can say how many rows are under it and, on an empty
// section, show a '+' in its chevron's place and fill the section when pressed, or say that the things it stands over hold different rows, and carry the button that reverts what is inside to its
// default, a switch saying where what is inside comes from, and controls of the section's own at its left edge and beside its chevron.
public class PanelSection : ContentControl
{
    private const string BannerPart = "Band";
    private const string RevertButtonPart = "Revert";
    private const string BannerSwitchPart = "Switch";
    private const string MixedWord = "mixed";

    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<PanelSection, string?>(nameof(Header));
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<PanelSection, bool>(nameof(IsOpen));
    public static readonly StyledProperty<int?> RowCountProperty = AvaloniaProperty.Register<PanelSection, int?>(nameof(RowCount));
    public static readonly StyledProperty<string?> RowWordProperty = AvaloniaProperty.Register<PanelSection, string?>(nameof(RowWord));
    public static readonly StyledProperty<int?> SwitchedOnCountProperty = AvaloniaProperty.Register<PanelSection, int?>(nameof(SwitchedOnCount));
    public static readonly StyledProperty<bool> CanAddProperty = AvaloniaProperty.Register<PanelSection, bool>(nameof(CanAdd), true);
    public static readonly StyledProperty<string?> RevertTooltipProperty = AvaloniaProperty.Register<PanelSection, string?>(nameof(RevertTooltip));
    public static readonly StyledProperty<bool> IsMixedProperty = AvaloniaProperty.Register<PanelSection, bool>(nameof(IsMixed));
    public static readonly StyledProperty<bool> CanRevertProperty = AvaloniaProperty.Register<PanelSection, bool>(nameof(CanRevert));
    public static readonly StyledProperty<object?> BannerLeftContentProperty = AvaloniaProperty.Register<PanelSection, object?>(nameof(BannerLeftContent));
    public static readonly StyledProperty<object?> BannerRightContentProperty = AvaloniaProperty.Register<PanelSection, object?>(nameof(BannerRightContent));
    public static readonly StyledProperty<object?> BannerMiddleContentProperty = AvaloniaProperty.Register<PanelSection, object?>(nameof(BannerMiddleContent));
    public static readonly StyledProperty<IBrush?> BannerBackgroundProperty = AvaloniaProperty.Register<PanelSection, IBrush?>(nameof(BannerBackground));

    private ToggleButton? _banner;
    private Button? _revertButton;
    private IconSwitch? _bannerSwitch;
    private IReadOnlyList<Geometry>? _switchIcons;
    private int? _switchState = 0;
    private int _switchMixedGoesTo;

    // The word on the banner, written in capitals like every other header.
    public string? Header
    {
        get
        {
            return GetValue(HeaderProperty);
        }
        set
        {
            SetValue(HeaderProperty, value);
        }
    }

    // Whether the section stands open.
    public bool IsOpen
    {
        get
        {
            return GetValue(IsOpenProperty);
        }
        set
        {
            SetValue(IsOpenProperty, value);
        }
    }

    // How many rows the body holds, or null where the section is not a list of rows, which shows neither a count nor a '+'.
    public int? RowCount
    {
        get
        {
            return GetValue(RowCountProperty);
        }
        set
        {
            SetValue(RowCountProperty, value);
        }
    }

    // A word the banner shows in place of the row count, or null to show the count.
    public string? RowWord
    {
        get
        {
            return GetValue(RowWordProperty);
        }
        set
        {
            SetValue(RowWordProperty, value);
        }
    }

    // How many of the body's rows are switched on, which the banner shows in place of a row count for a section that is not a list.
    public int? SwitchedOnCount
    {
        get
        {
            return GetValue(SwitchedOnCountProperty);
        }
        set
        {
            SetValue(SwitchedOnCountProperty, value);
        }
    }

    // Whether one more row would fit, which lets an empty section's banner be pressed.
    public bool CanAdd
    {
        get
        {
            return GetValue(CanAddProperty);
        }
        set
        {
            SetValue(CanAddProperty, value);
        }
    }

    // The tooltip of the revert button the banner shows while anything in the section is off its default.
    public string? RevertTooltip
    {
        get
        {
            return GetValue(RevertTooltipProperty);
        }
        set
        {
            SetValue(RevertTooltipProperty, value);
        }
    }

    // Whether the things the section stands over hold different rows, which shuts the section.
    public bool IsMixed
    {
        get
        {
            return GetValue(IsMixedProperty);
        }
        set
        {
            SetValue(IsMixedProperty, value);
        }
    }

    // Whether any of the things the section stands over is off its default, which shows the revert button.
    public bool CanRevert
    {
        get
        {
            return GetValue(CanRevertProperty);
        }
        set
        {
            SetValue(CanRevertProperty, value);
        }
    }

    // Controls of the section's own standing at the banner's left edge, pressed without folding the section, or null for none.
    public object? BannerLeftContent
    {
        get
        {
            return GetValue(BannerLeftContentProperty);
        }
        set
        {
            SetValue(BannerLeftContentProperty, value);
        }
    }

    // Controls of the section's own standing on the banner to the left of its chevron, pressed without folding the section, or null for none.
    public object? BannerRightContent
    {
        get
        {
            return GetValue(BannerRightContentProperty);
        }
        set
        {
            SetValue(BannerRightContentProperty, value);
        }
    }

    // What the section shows in the middle of its banner, taking no press and hidden while the section is mixed, or null for none.
    public object? BannerMiddleContent
    {
        get
        {
            return GetValue(BannerMiddleContentProperty);
        }
        set
        {
            SetValue(BannerMiddleContentProperty, value);
        }
    }

    // The fill of the banner, the body's being the section's own background.
    public IBrush? BannerBackground
    {
        get
        {
            return GetValue(BannerBackgroundProperty);
        }
        set
        {
            SetValue(BannerBackgroundProperty, value);
        }
    }

    // The glyphs the banner's switch cycles through, or null for a section with no switch.
    public IReadOnlyList<Geometry>? SwitchIcons
    {
        get
        {
            return _switchIcons;
        }
        set
        {
            _switchIcons = value;
            ShowSwitch();
        }
    }

    // Which state the banner's switch shows, or null for its mixed state.
    public int? SwitchState
    {
        get
        {
            return _switchState;
        }
        set
        {
            _switchState = value;
            ShowSwitch();
        }
    }

    // Where a press on the banner's switch out of its mixed state goes.
    public int SwitchMixedGoesTo
    {
        get
        {
            return _switchMixedGoesTo;
        }
        set
        {
            _switchMixedGoesTo = value;
            ShowSwitch();
        }
    }

    // Whether the section is a list holding no rows, which shows the '+' and makes a press on the banner add the first row.
    private bool IsEmpty
    {
        get
        {
            return RowCount == 0 && !IsMixed;
        }
    }

    // Raised with the state a press on the banner's switch asked for.
    public event Action<int>? SwitchCycled;

    // Raised by a press on an empty section's banner, after the section has been opened.
    public event EventHandler<RoutedEventArgs>? AddRequested;

    // Raised by the banner's revert button, which puts what is inside back at its default, and folds nothing itself.
    public event EventHandler<RoutedEventArgs>? RevertRequested;

    // Lays the opened body out at once and scrolls the panel so the whole section stands in sight clear of the panel's edge fades.
    public void ScrollWholeIntoViewOnceLaidOut()
    {
        UpdateLayout();
        this.BringIntoView(new Rect(Bounds.Size).Inflate(new Thickness(0, Palette.PanelFadeHeight)));
    }

    // Finds the banner's parts in the new template, moves the handlers onto them and draws the banner.
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UnhookTemplateParts();

        _banner = e.NameScope.Find<ToggleButton>(BannerPart);
        _revertButton = e.NameScope.Find<Button>(RevertButtonPart);
        _bannerSwitch = e.NameScope.Find<IconSwitch>(BannerSwitchPart);

        HookTemplateParts();
        ShowSwitch();
        ShowBanner();
    }

    // Folds the section when its last row goes or it turns mixed, and redraws the banner when anything it shows changes.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowCountProperty && RowCount == 0)
        {
            IsOpen = false;
        }

        if (change.Property == IsMixedProperty && IsMixed)
        {
            IsOpen = false;
        }

        if (change.Property == IsOpenProperty || change.Property == RowCountProperty || change.Property == RowWordProperty || change.Property == SwitchedOnCountProperty
            || change.Property == CanAddProperty || change.Property == IsMixedProperty || change.Property == CanRevertProperty)
        {
            ShowBanner();
        }
    }

    // Takes the handlers off the parts of the template being replaced.
    private void UnhookTemplateParts()
    {
        if (_banner is not null)
        {
            _banner.IsCheckedChanged -= OnBannerToggled;
        }

        if (_revertButton is not null)
        {
            _revertButton.Click -= OnRevertClick;
        }

        if (_bannerSwitch is not null)
        {
            _bannerSwitch.Cycled -= OnSwitchCycled;
        }
    }

    // Puts the handlers on the parts of the template just applied.
    private void HookTemplateParts()
    {
        if (_banner is not null)
        {
            _banner.IsCheckedChanged += OnBannerToggled;
        }

        if (_revertButton is not null)
        {
            _revertButton.Click += OnRevertClick;
        }

        if (_bannerSwitch is not null)
        {
            _bannerSwitch.Cycled += OnSwitchCycled;
        }
    }

    // Opens or folds the section as the banner is pressed, or opens an empty one with its first row.
    private void OnBannerToggled(object? sender, RoutedEventArgs e)
    {
        bool isPressedWhileEmpty = IsEmpty && _banner?.IsChecked != IsOpen;

        if (isPressedWhileEmpty)
        {
            OpenWithFirstRow(e);
            return;
        }

        bool isOpening = _banner?.IsChecked == true && !IsOpen;
        IsOpen = _banner?.IsChecked == true;

        if (isOpening)
        {
            ScrollWholeIntoViewOnceLaidOut();
        }
    }

    // Opens the section and asks for the first row in one press.
    private void OpenWithFirstRow(RoutedEventArgs e)
    {
        IsOpen = true;
        AddRequested?.Invoke(this, e);
        ScrollWholeIntoViewOnceLaidOut();
    }

    // Asks for what is inside the section to be put back at its default.
    private void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        RevertRequested?.Invoke(this, e);
    }

    // Passes on the state a press on the banner's switch asked for.
    private void OnSwitchCycled(int state)
    {
        SwitchCycled?.Invoke(state);
    }

    // Hands the banner's switch its glyphs and state, and shows it only where it has glyphs.
    private void ShowSwitch()
    {
        if (_bannerSwitch is null)
        {
            return;
        }

        _bannerSwitch.Icons = _switchIcons;
        _bannerSwitch.State = _switchState;
        _bannerSwitch.MixedGoesTo = _switchMixedGoesTo;
        _bannerSwitch.IsVisible = _switchIcons is { Count: > 0 };
    }

    // Shows the revert button where the section calls for it, the chevron, the '+' in its place or neither, whether the banner can be pressed, and the row count or that the rows are mixed.
    private void ShowBanner()
    {
        bool isEmptyAndFull = IsEmpty && !CanAdd;

        if (_revertButton is not null)
        {
            _revertButton.IsVisible = CanRevert;
        }

        if (_banner is null)
        {
            return;
        }

        _banner.IsChecked = IsOpen;
        _banner.Classes.Set(ThemeClasses.ChevronHidden, IsMixed);
        _banner.Classes.Set(ThemeClasses.AddsFirstRow, IsEmpty);
        _banner.Classes.Set(ThemeClasses.Inert, isEmptyAndFull);
        _banner.IsHitTestVisible = !IsMixed && !isEmptyAndFull;
        _banner.Tag = BannerCountText();
    }

    // What the middle of the banner says: that the rows are mixed, the word the rows give in place of a count, how many rows there are or how many are
    // switched on, or nothing where that is none.
    private string? BannerCountText()
    {
        if (IsMixed)
        {
            return $"({MixedWord})";
        }

        if (RowWord is string rowWord)
        {
            return $"({rowWord})";
        }

        int? shownCount = RowCount ?? SwitchedOnCount;
        return shownCount is > 0 ? $"({shownCount})" : null;
    }
}
