using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace SmartSQL.Desktop.Queries;

/// <summary>A native TextBox editor with color-only text runs; editing/caret/selection remain Avalonia's.</summary>
public sealed class SqlTextEditor : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public SqlTextEditor()
    {
        AcceptsReturn = true;
        AcceptsTab = true;
        TextWrapping = TextWrapping.NoWrap;
        FontFamily = new FontFamily("Consolas,Menlo,monospace");
        Template = new FuncControlTemplate<SqlTextEditor>((editor, scope) =>
        {
            var presenter = new SqlTextPresenter { Name = "PART_TextPresenter", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = new Cursor(StandardCursorType.Ibeam) };
            scope.Register(presenter.Name, presenter);
            void Present<T>(StyledProperty<T> property, string source, BindingMode mode = BindingMode.OneWay) =>
                presenter.Bind(property, new Binding(source) { Source = editor, Mode = mode });
            Present(TextPresenter.TextProperty, nameof(Text), BindingMode.TwoWay);
            Present(TextPresenter.CaretIndexProperty, nameof(CaretIndex));
            Present(TextPresenter.CaretBlinkIntervalProperty, nameof(CaretBlinkInterval));
            Present(TextPresenter.SelectionStartProperty, nameof(SelectionStart));
            Present(TextPresenter.SelectionEndProperty, nameof(SelectionEnd));
            Present(TextPresenter.SelectionBrushProperty, nameof(SelectionBrush));
            Present(TextPresenter.SelectionForegroundBrushProperty, nameof(SelectionForegroundBrush));
            Present(TextPresenter.CaretBrushProperty, nameof(CaretBrush));
            Present(TextPresenter.TextWrappingProperty, nameof(TextWrapping));
            Present(TextPresenter.TextAlignmentProperty, nameof(TextAlignment));
            Present(TextPresenter.LineHeightProperty, nameof(LineHeight));
            Present(TextPresenter.LetterSpacingProperty, nameof(LetterSpacing));
            Present(TextElement.FontFamilyProperty, nameof(FontFamily));
            Present(TextElement.FontSizeProperty, nameof(FontSize));
            Present(TextElement.FontStyleProperty, nameof(FontStyle));
            Present(TextElement.FontWeightProperty, nameof(FontWeight));
            Present(TextElement.FontStretchProperty, nameof(FontStretch));
            Present(TextElement.FontFeaturesProperty, nameof(FontFeatures));
            Present(TextElement.ForegroundProperty, nameof(Foreground));
            var scroll = new ScrollViewer
            {
                Name = "PART_ScrollViewer", Content = presenter,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            scroll.Bind(PaddingProperty, new Binding(nameof(Padding)) { Source = editor });
            scope.Register(scroll.Name, scroll);
            var border = new Border { Name = "PART_BorderElement", Child = scroll };
            scope.Register(border.Name, border);
            border.Bind(Border.BackgroundProperty, new Binding(nameof(Background)) { Source = editor, Priority = BindingPriority.Template });
            border.Bind(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { Source = editor, Priority = BindingPriority.Template });
            border.Bind(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness)) { Source = editor, Priority = BindingPriority.Template });
            border.Bind(Border.CornerRadiusProperty, new Binding(nameof(CornerRadius)) { Source = editor, Priority = BindingPriority.Template });
            return border;
        });
    }
}

/// <summary>Uses the same TextLayout for rendering and Avalonia hit testing; no synchronized overlay.</summary>
public sealed class SqlTextPresenter : TextPresenter
{
    public static readonly StyledProperty<IBrush?> KeywordBrushProperty = AvaloniaProperty.Register<SqlTextPresenter, IBrush?>(nameof(KeywordBrush));
    public static readonly StyledProperty<IBrush?> StringBrushProperty = AvaloniaProperty.Register<SqlTextPresenter, IBrush?>(nameof(StringBrush));
    public static readonly StyledProperty<IBrush?> CommentBrushProperty = AvaloniaProperty.Register<SqlTextPresenter, IBrush?>(nameof(CommentBrush));
    public static readonly StyledProperty<IBrush?> NumberBrushProperty = AvaloniaProperty.Register<SqlTextPresenter, IBrush?>(nameof(NumberBrush));
    public static readonly StyledProperty<IBrush?> IdentifierBrushProperty = AvaloniaProperty.Register<SqlTextPresenter, IBrush?>(nameof(IdentifierBrush));
    public IBrush? KeywordBrush { get => GetValue(KeywordBrushProperty); set => SetValue(KeywordBrushProperty, value); }
    public IBrush? StringBrush { get => GetValue(StringBrushProperty); set => SetValue(StringBrushProperty, value); }
    public IBrush? CommentBrush { get => GetValue(CommentBrushProperty); set => SetValue(CommentBrushProperty, value); }
    public IBrush? NumberBrush { get => GetValue(NumberBrushProperty); set => SetValue(NumberBrushProperty, value); }
    public IBrush? IdentifierBrush { get => GetValue(IdentifierBrushProperty); set => SetValue(IdentifierBrushProperty, value); }
    private string? _highlightedText;
    private IReadOnlyList<SqlHighlightSpan> _highlights = [];

    public SqlTextPresenter()
    {
        this.Bind(KeywordBrushProperty, new DynamicResourceExtension("UiSqlKeywordBrush"));
        this.Bind(StringBrushProperty, new DynamicResourceExtension("UiSqlStringBrush"));
        this.Bind(CommentBrushProperty, new DynamicResourceExtension("UiSqlCommentBrush"));
        this.Bind(NumberBrushProperty, new DynamicResourceExtension("UiSqlNumberBrush"));
        this.Bind(IdentifierBrushProperty, new DynamicResourceExtension("UiSqlIdentifierBrush"));
    }

    protected override TextLayout CreateTextLayout()
    {
        // During IME composition use Avalonia's native preedit underline/layout unchanged.
        // The editor is intentionally NoWrap; native behavior handles any other usage.
        if (!string.IsNullOrEmpty(PreeditText) || TextWrapping != TextWrapping.NoWrap || PasswordChar != default)
            return base.CreateTextLayout();
        var text = Text ?? string.Empty;
        if (!string.Equals(text, _highlightedText, StringComparison.Ordinal))
        {
            _highlights = SqlSyntaxHighlighting.Scan(text);
            _highlightedText = text;
        }
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionLength = Math.Abs(SelectionEnd - SelectionStart);
        var selected = ShowSelectionHighlight && SelectionForegroundBrush is not null && selectionLength > 0;
        var highlights = selected ? SqlSyntaxHighlighting.ExcludeSelection(_highlights, selectionStart, selectionLength) : _highlights;
        // Color-only run properties are shared by every span of a kind in this layout.
        TextRunProperties Properties(IBrush? brush) => new GenericTextRunProperties(typeface, FontSize,
            foregroundBrush: brush ?? Foreground, fontFeatures: FontFeatures);
        TextRunProperties[] properties = [Properties(KeywordBrush), Properties(StringBrush),
            Properties(CommentBrush), Properties(NumberBrush), Properties(IdentifierBrush)];
        var runs = new List<ValueSpan<TextRunProperties>>(highlights.Count + 1);
        var selectionPending = selected;
        void AddSelection()
        {
            runs.Add(new(selectionStart, selectionLength, Properties(SelectionForegroundBrush)));
            selectionPending = false;
        }
        // ExcludeSelection keeps source order and removes overlap. Insert the one native
        // selection foreground at its sorted position, without sorting all syntax runs.
        foreach (var span in highlights)
        {
            if (selectionPending && selectionStart <= span.Start) AddSelection();
            runs.Add(new(span.Start, span.Length, properties[(int)span.Kind]));
        }
        if (selectionPending) AddSelection();
        return new TextLayout(text, typeface, FontSize, Foreground, TextAlignment, TextWrapping.NoWrap,
            flowDirection: FlowDirection, maxWidth: double.PositiveInfinity, maxHeight: double.PositiveInfinity,
            lineHeight: LineHeight, letterSpacing: LetterSpacing, fontFeatures: FontFeatures, textStyleOverrides: runs);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeywordBrushProperty || change.Property == StringBrushProperty || change.Property == CommentBrushProperty
            || change.Property == NumberBrushProperty || change.Property == IdentifierBrushProperty)
            InvalidateTextLayout();
    }
}
