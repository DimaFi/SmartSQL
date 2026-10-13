using Avalonia.Controls;
using Avalonia.Media;

namespace SmartSQL.Desktop.Presentation;

/// <summary>Creates native vector icons with the foreground inherited from their control.</summary>
public static class UiIcon
{
    public static PathIcon Create(string resourceKey, double size = 18)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (!double.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        var app = Avalonia.Application.Current;
        if (app is null || !app.TryFindResource(resourceKey, out var value) || value is not Geometry geometry)
            throw new ArgumentException($"Не найден векторный ресурс: {resourceKey}", nameof(resourceKey));

        return new PathIcon { Data = geometry, Width = size, Height = size };
    }
}
