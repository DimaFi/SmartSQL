using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

public sealed record ConnectionRequest(ConnectionProfile Profile, string Password, bool AllowEditing = false)
{
    public override string ToString() => $"{Profile.Name} ({Profile.Provider})";
}

/// <summary>Edits nonsecret profiles and returns a transient connection request.</summary>
public sealed class ConnectionsDialog : Window
{
    private readonly IConnectionProfileStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ListBox _profilesList = new();
    private readonly TextBox _name = new() { PlaceholderText = "Например, Рабочая база" };
    private readonly TextBox _host = new() { PlaceholderText = "localhost", Text = "localhost" };
    private readonly TextBox _port = new() { Text = "5432" };
    private readonly TextBox _database = new() { PlaceholderText = "Имя базы данных" };
    private readonly TextBox _username = new() { PlaceholderText = "Пользователь PostgreSQL" };
    private readonly TextBox _password = new() { PasswordChar = '●', PlaceholderText = "Пароль для этого подключения" };
    private readonly CheckBox _allowEditing = new() { Content = "Разрешить редактирование данных" };
    private readonly ComboBox _ssl = new();
    private readonly TextBlock _sslHint = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "error" } };
    private readonly Button _save = new() { Content = "Сохранить профиль", Classes = { "secondary" } };
    private readonly Button _delete = new() { Content = "Удалить профиль", IsEnabled = false, Classes = { "quiet", "danger" } };
    private readonly Button _add = new() { Content = "+ Новый профиль", Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _connect = new() { Content = "Подключиться", Classes = { "primary" } };
    private readonly StackPanel _editor = new() { Spacing = 9 };
    private readonly TextBlock _providerLabel = new() { Text = "PostgreSQL · просмотр базы", FontSize = 19, FontWeight = FontWeight.SemiBold };
    private IReadOnlyList<ConnectionProfile> _profiles = [];
    private Guid? _selectedId;
    private string _selectedProvider = DatabaseProviders.PostgreSql;
    private bool _busy;
    private bool _closed;
    private bool _storeLoaded;
    private bool _submitted;

    private sealed record ProfileChoice(ConnectionProfile Profile)
    {
        public override string ToString() => $"{Profile.Name}\n{Profile.Host}:{Profile.Port} / {Profile.Database}";
    }

    private sealed record SslChoice(ConnectionSslPolicy Policy, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly SslChoice[] SslChoices =
    [
        new(ConnectionSslPolicy.VerifyFull, "Проверять сертификат и имя сервера"),
        new(ConnectionSslPolicy.VerifyCA, "Проверять сертификат"),
        new(ConnectionSslPolicy.Require, "Требовать шифрование"),
        new(ConnectionSslPolicy.Prefer, "Предпочитать шифрование"),
        new(ConnectionSslPolicy.Disable, "Без шифрования")
    ];

    public ConnectionsDialog(IConnectionProfileStore store)
    {
        _store = store;
        Classes.Add("dialog");
        Title = "Подключения · SmartSQL";
        Width = 960;
        Height = 760;
        MinWidth = 740;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ssl.ItemsSource = SslChoices;
        _ssl.SelectedIndex = 0;
        _ssl.SelectionChanged += (_, _) => UpdateSslHint();
        _allowEditing.IsCheckedChanged += (_, _) => UpdateProviderLabel();
        _profilesList.SelectionChanged += (_, _) => SelectProfile();
        _add.Click += (_, _) => NewProfile();
        _save.Click += async (_, _) => await SaveProfileAsync();
        _delete.Click += async (_, _) => await DeleteProfileAsync();
        _connect.Click += (_, _) => Connect();
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(null);
        Opened += async (_, _) => await LoadProfilesAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _password.Text = string.Empty;
            _allowEditing.IsChecked = false;
            _lifetime.Dispose();
        };

        _profilesList.Classes.Add("navigation");
        _profilesList.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var profilePane = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        profilePane.Children.Add(new TextBlock { Text = "Сохранённые подключения", Classes = { "sectionTitle" }, TextWrapping = TextWrapping.Wrap });
        Add(profilePane, _profilesList, 1);
        Add(profilePane, _add, 2);
        _editor.Children.Add(_providerLabel);
        _editor.Children.Add(new TextBlock { Text = "Имя профиля" });
        _editor.Children.Add(_name);
        var server = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110"), ColumnSpacing = 10 };
        server.Children.Add(Field("Сервер", _host));
        var portField = Field("Порт", _port);
        Grid.SetColumn(portField, 1);
        server.Children.Add(portField);
        _editor.Children.Add(server);
        _editor.Children.Add(Field("База данных", _database));
        _editor.Children.Add(Field("Пользователь", _username));
        _editor.Children.Add(Field("Защита соединения", _ssl));
        _editor.Children.Add(_sslHint);
        _editor.Children.Add(Field("Пароль", _password));
        _editor.Children.Add(new TextBlock
        {
            Text = "Пароль используется только для текущего подключения и не сохраняется. Без отдельного разрешения внешняя база открывается только для просмотра.",
            TextWrapping = TextWrapping.Wrap, Classes = { "muted" }
        });
        _editor.Children.Add(_allowEditing);
        _editor.Children.Add(new TextBlock
        {
            Text = "Разрешение действует только для этого подключения. SmartSQL позволяет добавить строку, изменить одну ячейку или удалить выбранную строку, если структура, права и связи таблицы допускают безопасную операцию. Удаление требует отдельного подтверждения.",
            TextWrapping = TextWrapping.Wrap, Classes = { "caption" }
        });
        _save.Margin = new Thickness(0, 0, 8, 8);
        _delete.Margin = new Thickness(0, 0, 0, 8);
        _editor.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { _save, _delete } });
        _editor.Children.Add(new TextBlock
        {
            Text = "Удаление профиля удаляет только параметры подключения из SmartSQL.",
            TextWrapping = TextWrapping.Wrap, Classes = { "caption" }
        });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 16 };
        body.Children.Add(new Border { Classes = { "card" }, Child = profilePane });
        var editorScroll = new ScrollViewer
        {
            Content = _editor,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var editorCard = new Border { Classes = { "card" }, Child = editorScroll };
        Grid.SetColumn(editorCard, 1);
        body.Children.Add(editorCard);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 14 };
        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(new TextBlock { Text = "Подключения к базам данных", Classes = { "pageTitle" }, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = "Выберите сохранённый профиль или укажите параметры сервера.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(heading);
        Add(layout, body, 1);
        Add(layout, _status, 2);
        Add(layout, new Border
        {
            Classes = { "card" }, Padding = new Thickness(12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { cancel, _connect }
            }
        }, 3);
        Content = layout;
        UpdateSslHint();
        UpdateControls();
    }

    private static StackPanel Field(string label, Control input) => new() { Spacing = 5, Children = { new TextBlock { Text = label }, input } };

    private static void Add(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private void NewProfile()
    {
        if (_busy)
            return;
        _profilesList.SelectedItem = null;
        _selectedId = null;
        _selectedProvider = DatabaseProviders.PostgreSql;
        _providerLabel.Text = "PostgreSQL · просмотр базы";
        _name.Text = string.Empty;
        _host.Text = "localhost";
        _port.Text = "5432";
        _database.Text = string.Empty;
        _username.Text = string.Empty;
        _password.Text = string.Empty;
        _allowEditing.IsChecked = false;
        _ssl.SelectedIndex = 0;
        _status.Text = string.Empty;
        UpdateControls();
        _name.Focus();
    }

    private void SelectProfile()
    {
        if (_profilesList.SelectedItem is not ProfileChoice choice)
        {
            _selectedId = null;
            _allowEditing.IsChecked = false;
            UpdateControls();
            return;
        }
        var profile = choice.Profile;
        _selectedId = profile.Id;
        _selectedProvider = profile.Provider;
        _providerLabel.Text = profile.Provider == DatabaseProviders.PostgreSql ? "PostgreSQL · просмотр базы" : $"Провайдер: {profile.Provider}";
        _name.Text = profile.Name;
        _host.Text = profile.Host;
        _port.Text = profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _database.Text = profile.Database;
        _username.Text = profile.Username;
        _password.Text = string.Empty;
        _allowEditing.IsChecked = false;
        _ssl.SelectedItem = SslChoices.First(choice => choice.Policy == profile.SslPolicy);
        _status.Text = profile.Provider == DatabaseProviders.PostgreSql ? string.Empty : "Этот провайдер пока недоступен в интерфейсе. Создайте профиль PostgreSQL для подключения.";
        UpdateControls();
    }

    private ConnectionProfile ReadProfile()
    {
        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535)
            throw new ArgumentException("Укажите порт от 1 до 65535.");
        var profile = new ConnectionProfile(_selectedId ?? Guid.NewGuid(), _name.Text?.Trim() ?? string.Empty,
            _selectedProvider, _host.Text?.Trim() ?? string.Empty, port,
            _database.Text ?? string.Empty, _username.Text ?? string.Empty,
            (_ssl.SelectedItem as SslChoice)?.Policy ?? ConnectionSslPolicy.VerifyFull);
        profile.Validate();
        return profile;
    }

    private async Task LoadProfilesAsync()
    {
        await RunStoreOperationAsync(async cancellationToken =>
        {
            var profiles = await _store.LoadAsync(cancellationToken);
            if (_closed)
                return;
            _profiles = profiles;
            _storeLoaded = true;
            RefreshList(null);
            _status.Text = profiles.Count == 0 ? "Сохранённых профилей пока нет. Укажите параметры нового подключения." : string.Empty;
        }, "Не удалось загрузить профили. Проверьте доступ к файлу подключений; существующие профили не перезаписываются.");
    }

    private async Task SaveProfileAsync()
    {
        if (_busy || !_storeLoaded)
            return;
        ConnectionProfile profile;
        try
        {
            profile = ReadProfile();
        }
        catch (ArgumentException exception)
        {
            _status.Text = exception.Message;
            return;
        }
        var updated = _profiles.Where(item => item.Id != profile.Id).Append(profile).ToArray();
        await RunStoreOperationAsync(async cancellationToken =>
        {
            await _store.SaveAsync(updated, cancellationToken);
            if (_closed)
                return;
            _profiles = updated;
            RefreshList(profile.Id);
            _status.Text = "Профиль сохранён. Пароль не сохранялся.";
        }, "Не удалось сохранить профиль. Проверьте доступ к файлу подключений.");
    }

    private async Task DeleteProfileAsync()
    {
        if (_busy || !_storeLoaded || _selectedId is not { } selectedId)
            return;
        var updated = _profiles.Where(item => item.Id != selectedId).ToArray();
        await RunStoreOperationAsync(async cancellationToken =>
        {
            await _store.SaveAsync(updated, cancellationToken);
            if (_closed)
                return;
            _profiles = updated;
            RefreshList(null);
            _selectedId = null;
            _password.Text = string.Empty;
            _allowEditing.IsChecked = false;
            _status.Text = "Профиль удалён из SmartSQL.";
        }, "Не удалось удалить профиль. Проверьте доступ к файлу подключений.");
    }

    private void RefreshList(Guid? selectedId)
    {
        var choices = _profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).Select(profile => new ProfileChoice(profile)).ToArray();
        _profilesList.ItemsSource = choices;
        _profilesList.SelectedItem = choices.FirstOrDefault(choice => choice.Profile.Id == selectedId);
    }

    private async Task RunStoreOperationAsync(Func<CancellationToken, Task> action, string failureMessage)
    {
        if (_busy || _closed)
            return;
        _busy = true;
        UpdateControls();
        try
        {
            await action(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (FormatException) when (!_storeLoaded)
        {
            if (!_closed)
                _status.Text = "Файл профилей содержит неподдерживаемые или повреждённые данные. Проверьте его формат; существующий файл не перезаписывается.";
        }
        catch (Exception)
        {
            if (!_closed)
                _status.Text = failureMessage;
        }
        finally
        {
            _busy = false;
            if (!_closed)
                UpdateControls();
        }
    }

    private void Connect()
    {
        if (_busy || _submitted || _selectedProvider != DatabaseProviders.PostgreSql)
            return;
        try
        {
            var profile = ReadProfile();
            var password = _password.Text ?? string.Empty;
            var allowEditing = _allowEditing.IsChecked == true;
            _submitted = true;
            _password.Text = string.Empty;
            _allowEditing.IsChecked = false;
            UpdateControls();
            Close(new ConnectionRequest(profile, password, allowEditing));
        }
        catch (ArgumentException exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void UpdateControls()
    {
        _profilesList.IsEnabled = !_busy;
        _editor.IsEnabled = !_busy;
        _allowEditing.IsEnabled = !_busy && !_submitted && _selectedProvider == DatabaseProviders.PostgreSql;
        _add.IsEnabled = !_busy;
        _save.IsEnabled = !_busy && _storeLoaded;
        _delete.IsEnabled = !_busy && _storeLoaded && _selectedId is not null;
        _connect.IsEnabled = !_busy && !_submitted && _selectedProvider == DatabaseProviders.PostgreSql;
    }

    private void UpdateSslHint()
    {
        _sslHint.Text = (_ssl.SelectedItem as SslChoice)?.Policy switch
        {
            ConnectionSslPolicy.Disable => "Соединение не шифруется. Используйте только в подходящей локальной среде.",
            ConnectionSslPolicy.Prefer => "Если сервер не поддерживает шифрование, подключение возможно без него.",
            ConnectionSslPolicy.Require => "Сервер должен поддерживать шифрование.",
            ConnectionSslPolicy.VerifyCA => "Проверяется доверие к сертификату сервера.",
            _ => "Проверяются сертификат и соответствие имени сервера. Рекомендуемый режим."
        };
    }

    private void UpdateProviderLabel()
    {
        if (_selectedProvider != DatabaseProviders.PostgreSql)
            return;
        _providerLabel.Text = _allowEditing.IsChecked == true
            ? "PostgreSQL · просмотр и разрешённые изменения"
            : "PostgreSQL · просмотр базы";
    }
}
