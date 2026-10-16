using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class DeleteSchemaDialog : Window
{
    private readonly IDataWorkspace _workspace;
    private readonly TableDefinition _table;
    private readonly bool _deleteTable;
    private readonly ComboBox _fieldBox = new();
    private readonly TextBlock _impact = new() { TextWrapping = TextWrapping.Wrap, Classes = { "warning" } };
    private readonly TextBox _confirmation = new() { PlaceholderText = "Введите название для подтверждения" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "error" } };

    public DeleteSchemaDialog(IDataWorkspace workspace, TableDefinition table, bool deleteTable)
    {
        _workspace = workspace;
        _table = table;
        _deleteTable = deleteTable;
        Title = deleteTable ? "Удаление таблицы · SmartSQL" : "Удаление поля · SmartSQL";
        Width = 540;
        Height = deleteTable ? 390 : 450;
        MinWidth = 460;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (!deleteTable)
        {
            _fieldBox.ItemsSource = table.Fields;
            _fieldBox.SelectedIndex = 0;
            _fieldBox.SelectionChanged += (_, _) => UpdateImpact();
        }
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var delete = new Button { Content = deleteTable ? "Удалить таблицу" : "Удалить поле" };
        delete.Click += (_, _) => Delete();

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = deleteTable ? "Безвозвратное удаление таблицы" : "Безвозвратное удаление поля",
            FontSize = 23,
            FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "SmartSQL сначала создаст резервную копию базы. Для восстановления потребуется открыть файл этой копии вручную.",
            TextWrapping = TextWrapping.Wrap
        });
        if (!deleteTable)
            content.Children.Add(_fieldBox);
        content.Children.Add(_impact);
        content.Children.Add(new TextBlock
        {
            Text = deleteTable
                ? $"Для подтверждения введите название таблицы: {_table.Name}"
                : "Для подтверждения введите название выбранного поля.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_confirmation);
        content.Children.Add(_error);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, delete }
        });
        Content = new Border { Padding = new Thickness(24), Child = content };
        UpdateImpact();
    }

    private void UpdateImpact()
    {
        try
        {
            _confirmation.Text = string.Empty;
            var impact = _deleteTable
                ? _workspace.GetTableDeletionImpact(_table.Id)
                : _workspace.GetFieldDeletionImpact(_table.Id, SelectedField.Id);
            _impact.Text = _deleteTable
                ? $"Будут удалены все записи: {impact.RecordCount}; сохранённые фильтры: {impact.AffectedSavedFilterCount}."
                : $"Значение заполнено в {impact.RecordCount} записях. Будут удалены зависимые сохранённые фильтры: {impact.AffectedSavedFilterCount}.";
            _error.Text = !_deleteTable && _table.Fields.Count <= 1
                ? "Это последнее поле таблицы. Вместо него удалите таблицу целиком."
                : string.Empty;
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }

    private FieldDefinition SelectedField => (FieldDefinition?)_fieldBox.SelectedItem
        ?? throw new InvalidOperationException("Выберите поле.");

    private void Delete()
    {
        try
        {
            if (!_deleteTable && _table.Fields.Count <= 1)
                throw new InvalidOperationException("Нельзя удалить последнее поле таблицы.");
            var expected = _deleteTable ? _table.Name : SelectedField.Name;
            if (!string.Equals(_confirmation.Text?.Trim(), expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Введите точное название «{expected}».");
            var result = _deleteTable
                ? _workspace.DeleteTable(_table.Id)
                : _workspace.DeleteField(_table.Id, SelectedField.Id);
            Close(result);
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}
