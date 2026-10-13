namespace SmartSQL.Application;

/// <summary>Provider-neutral, read-only page options. Search matches literal text, not a pattern.</summary>
public sealed record DatabaseBrowseQuery(string? SearchText = null, string? SortColumn = null, bool Descending = false)
{
    public const int MaxSearchCharacters = 256;

    public void Validate()
    {
        if (SearchText?.Length > MaxSearchCharacters)
            throw new ArgumentException($"Поиск ограничен {MaxSearchCharacters} символами.", nameof(SearchText));
        ValidateText(SearchText, nameof(SearchText));
        ValidateText(SortColumn, nameof(SortColumn));
        if (SortColumn is { Length: 0 })
            throw new ArgumentException("Выберите существующий столбец сортировки.", nameof(SortColumn));
    }

    private static void ValidateText(string? value, string parameter)
    {
        if (value is null)
            return;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\0')
                throw new ArgumentException("Текст содержит недопустимый нулевой символ.", parameter);
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                index++;
            else if (char.IsSurrogate(value[index]))
                throw new ArgumentException("Текст содержит некорректный Unicode.", parameter);
        }
    }
}
