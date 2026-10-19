namespace SmartSQL.Desktop.Views;

/// <summary>
/// Moves a field by one place in the user's visual and creation order.
/// This is presentation/input order only; it never changes an existing database schema.
/// </summary>
public static class FieldRowOrdering
{
    public static bool MoveByOne<T>(IList<T> rows, int index, int direction)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (direction != -1 && direction != 1)
            throw new ArgumentOutOfRangeException(nameof(direction), "Направление должно быть -1 или 1.");
        if (index < 0 || index >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var destination = index + direction;
        if (destination < 0 || destination >= rows.Count)
            return false;

        var row = rows[index];
        rows.RemoveAt(index);
        rows.Insert(destination, row);
        return true;
    }
}
