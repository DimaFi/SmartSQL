namespace SmartSQL.Application;

/// <summary>Provider-neutral, read-only page options. Search matches literal text, not a pattern.</summary>
public sealed record DatabaseBrowseQuery(string? SearchText = null, string? SortColumn = null, bool Descending = false,
    IReadOnlyList<DatabaseFilter>? Filters = null)
{
    public const int MaxSearchCharacters = 256;
    public const int MaxFilters = 20;
    public IReadOnlyList<DatabaseFilter> EffectiveFilters => Filters ?? [];

    /// <summary>Freeze caller-owned conditions before awaiting provider work.</summary>
    public DatabaseBrowseQuery Snapshot()
    {
        var snapshot = this with { Filters = EffectiveFilters.ToArray() };
        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (SearchText?.Length > MaxSearchCharacters)
            throw new ArgumentException($"Поиск ограничен {MaxSearchCharacters} символами.", nameof(SearchText));
        ValidateText(SearchText, nameof(SearchText));
        ValidateText(SortColumn, nameof(SortColumn));
        if (SortColumn is { Length: 0 })
            throw new ArgumentException("Выберите существующий столбец сортировки.", nameof(SortColumn));
        if (EffectiveFilters.Count > MaxFilters)
            throw new ArgumentException($"Можно объединить не более {MaxFilters} условий фильтра.", nameof(Filters));
        foreach (var filter in EffectiveFilters)
        {
            if (filter is null)
                throw new ArgumentException("Условие фильтра не может быть пустым.", nameof(Filters));
            filter.Validate();
        }
    }

    internal static void ValidateText(string? value, string parameter)
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
