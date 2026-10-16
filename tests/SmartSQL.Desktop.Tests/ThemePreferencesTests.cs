using System.Xml.Linq;
using SmartSQL.Desktop.Presentation;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class ThemePreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(TestRoot(), "theme-preferences", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingSettingDefaultsToGrayWithoutInventingAnOldLightPreference()
    {
        var store = Store();
        Assert.Equal(ApplicationTheme.Gray, store.Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(ApplicationTheme.Gray)]
    [InlineData(ApplicationTheme.Light)]
    public void ExplicitChoicePersistsAcrossInstancesAndDoesNotTouchOtherPreferences(ApplicationTheme theme)
    {
        Directory.CreateDirectory(_directory);
        var other = Path.Combine(_directory, "workspace-preferences.json");
        File.WriteAllText(other, "presentation mode sentinel");
        Store().Save(theme);
        Assert.Equal(theme, Store().Load());
        Assert.Equal("presentation mode sentinel", File.ReadAllText(other));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void SwitchingFromLightToGrayUpdatesOnlyTheExplicitChoice()
    {
        var store = Store();
        store.Save(ApplicationTheme.Light);
        store.Save(ApplicationTheme.Gray);
        Assert.Equal(ApplicationTheme.Gray, Store().Load());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ invalid")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"future\",\"theme\":\"Light\"}")]
    [InlineData("{\"format\":\"SmartSQL.ThemePreferences.v1\",\"theme\":\"light\"}")]
    [InlineData("{\"format\":\"SmartSQL.ThemePreferences.v1\",\"theme\":1}")]
    [InlineData("{\"format\":\"SmartSQL.ThemePreferences.v1\",\"theme\":\"Gray\",\"theme\":\"Light\"}")]
    [InlineData("{\"format\":\"SmartSQL.ThemePreferences.v1\",\"theme\":\"Gray\",\"mode\":\"Advanced\"}")]
    public void InvalidPreferenceIsReportedAndPreservedUntilAnExplicitNewChoice(string content)
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, content);
        var original = File.ReadAllBytes(store.FilePath);
        Assert.Throws<FormatException>(() => store.Load());
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        store.Save(ApplicationTheme.Light);
        Assert.Equal(ApplicationTheme.Light, store.Load());
    }

    [Fact]
    public void OversizedAndInvalidValuesFailWithoutModifyingTheCurrentFile()
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        var oversized = new byte[ThemePreferences.MaxFileBytes + 1];
        File.WriteAllBytes(store.FilePath, oversized);
        Assert.Throws<FormatException>(() => store.Load());
        Assert.Equal(oversized, File.ReadAllBytes(store.FilePath));
        store.Save(ApplicationTheme.Gray);
        var original = File.ReadAllBytes(store.FilePath);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save((ApplicationTheme)99));
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        Assert.Throws<ArgumentException>(() => new ThemePreferences("theme.json"));
    }

    [Fact]
    public void WindowsPreferencesRequireAnExplicitFileOnD()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => new ThemePreferences(@"C:\theme-preferences.json"));
        Assert.Throws<ArgumentException>(() => new ThemePreferences(@"D:\"));
    }

    [Fact]
    public void FailedAtomicReplacementPreservesTheExistingPreference()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store();
        store.Save(ApplicationTheme.Light);
        var original = File.ReadAllBytes(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(() => store.Save(ApplicationTheme.Gray));
            Assert.True(exception is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        Assert.Equal(ApplicationTheme.Light, store.Load());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void BothPalettesHaveMatchingSemanticKeysAndReadableTextOnTheirSurfaces()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var path = Path.Combine(SourceRoot(), "src", "SmartSQL.Desktop", "Styles", "Tokens.axaml");
        var dictionaries = XDocument.Load(path).Root!.Element(avalonia + "ResourceDictionary.ThemeDictionaries")!
            .Elements(avalonia + "ResourceDictionary").ToDictionary(element => (string)element.Attribute(xaml + "Key")!,
                element => element.Elements(avalonia + "SolidColorBrush").ToDictionary(brush => (string)brush.Attribute(xaml + "Key")!, brush => (string)brush.Attribute("Color")!));
        Assert.Equal(new[] { "Dark", "Light" }, dictionaries.Keys.Order());
        Assert.Equal(dictionaries["Light"].Keys.Order(), dictionaries["Dark"].Keys.Order());
        foreach (var palette in dictionaries.Values)
        {
            foreach (var surface in new[] { "UiCanvasBrush", "UiSurfaceBrush", "UiSurfaceMutedBrush", "UiPaperBrush" })
                foreach (var text in new[] { "UiTextBrush", "UiMutedTextBrush", "UiDangerBrush", "UiWarningBrush", "UiSuccessBrush" })
                    Assert.True(Contrast(palette[text], palette[surface]) >= 4.5,
                        $"{text} must stay readable on {surface}.");
            foreach (var background in new[] { "UiAccentBrush", "UiAccentHoverBrush" })
                Assert.True(Contrast(palette["UiOnAccentBrush"], palette[background]) >= 4.5);
            Assert.True(Contrast(palette["UiAccentTextBrush"], palette["UiAccentSoftBrush"]) >= 4.5);
            Assert.True(Contrast(palette["UiDisabledTextBrush"], palette["UiSurfaceBrush"]) >= 3.0);
            Assert.True(Contrast(palette["UiSelectionTextBrush"], palette["UiSelectionBrush"]) >= 4.5,
                "Selected text must be readable against its selection background.");
            foreach (var editorSurface in new[] { "UiSurfaceBrush", "UiPaperBrush" })
                Assert.True(Contrast(palette["UiSelectionBrush"], palette[editorSurface]) >= 3.0,
                    "The selected range must remain visibly distinct from the editor surface.");
            foreach (var sql in new[] { "UiSqlKeywordBrush", "UiSqlStringBrush", "UiSqlCommentBrush", "UiSqlNumberBrush", "UiSqlIdentifierBrush" })
                Assert.True(Contrast(palette[sql], palette["UiPaperBrush"]) >= 4.5, $"{sql} must be readable in the editor.");
        }
    }

    private static double Contrast(string first, string second)
    {
        static double Luminance(string hex)
        {
            static double Linear(byte value)
            {
                var channel = value / 255.0;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }
            var rgb = Convert.FromHexString(hex.TrimStart('#'));
            return Linear(rgb[0]) * 0.2126 + Linear(rgb[1]) * 0.7152 + Linear(rgb[2]) * 0.0722;
        }
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private ThemePreferences Store() => new(Path.Combine(_directory, "theme-preferences.json"));
    private static string TestRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_TEST_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return Path.Combine(SourceRoot(), ".workspace", "test-temp");
    }
    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SmartSQL.sln"))) return directory.FullName;
        throw new InvalidOperationException("Theme palette checks require the SmartSQL source workspace.");
    }
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
