using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartSQL.Desktop.Views;

public sealed class EditableRow
{
    public Guid Id { get; }
    public int Position { get; init; }
    public IReadOnlyList<EditableCell> Cells { get; }

    public EditableRow(Guid id, IReadOnlyList<EditableCell> cells)
    {
        Id = id;
        Cells = cells;
    }
}

public sealed class EditableCell : INotifyPropertyChanged
{
    private string? _value;
    private readonly Action<string?, string?> _save;
    private readonly Action<string> _reportError;

    public EditableCell(string? value, Action<string?, string?> save, Action<string> reportError)
    {
        _value = value;
        _save = save;
        _reportError = reportError;
    }

    public string? Value
    {
        get => _value;
        set
        {
            if (value == _value)
                return;
            try
            {
                _save(_value, value);
                _value = value;
            }
            catch (Exception exception)
            {
                _reportError($"Изменение не сохранено: {exception.Message}");
            }
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
