using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartSQL.Application;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private async void OnImportXlsx(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || !BeginTransfer()) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импортировать XLSX в выбранную таблицу",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(WorkspacePaths.DataDirectory),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Книга Excel") { Patterns = ["*.xlsx"] }]
            });
            if (files.Count == 0) { StatusText.Text = "Импорт отменён."; return; }
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный XLSX-файл.");
            if (new FileInfo(path).Length > 50_000_000) throw new IOException("XLSX-файл превышает предел 50 МБ.");
            var workspace = _workspace;
            var table = _currentTable;
            StatusText.Text = "Чтение XLSX…";
            var workbook = await Task.Run(() => { using var stream = File.OpenRead(path); return XlsxTableTransfer.Read(stream); });
            var sheet = await new XlsxSheetDialog(workbook).ShowDialog<string?>(GetDialogOwner());
            if (sheet is null) { StatusText.Text = "Импорт отменён. Данные не изменены."; return; }
            var source = await Task.Run(() => workbook.Inspect(table, sheet));
            var preview = await new CsvMappingDialog(table, workbook.GetCsvContent(sheet), source, "XLSX", allowDelimiter: false)
                .ShowDialog<CsvImportPreview?>(GetDialogOwner());
            if (preview is null || !await new CsvPreviewDialog(preview, "XLSX").ShowDialog<bool>(GetDialogOwner()))
            { StatusText.Text = "Импорт отменён. Данные не изменены."; return; }
            StatusText.Text = "Импорт XLSX…";
            var count = await Task.Run(() => preview.Commit(workspace));
            _offset = 0;
            RefreshRecords();
            StatusText.Text = $"Из листа «{sheet}» импортировано записей: {count}.";
        }
        catch (Exception exception) { StatusText.Text = $"Не удалось импортировать XLSX: {exception.Message}"; }
        finally { EndTransfer(); }
    }

    private async void OnExportXlsx(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || !BeginTransfer()) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспортировать всю таблицу в XLSX",
                SuggestedFileName = $"{_currentTable.Name}.xlsx",
                DefaultExtension = "xlsx",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(WorkspacePaths.DataDirectory)
            });
            if (file is null) { StatusText.Text = "Экспорт отменён."; return; }
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь для XLSX.");
            var workspace = _workspace;
            var tableId = _currentTable.Id;
            StatusText.Text = "Экспорт XLSX…";
            var result = await Task.Run(() =>
            {
                XlsxExportResult? result = null;
                AtomicFileExport.Create(path, stream => result = XlsxTableTransfer.Export(workspace, tableId, stream));
                return result!;
            });
            StatusText.Text = $"Экспортировано записей: {result.RowCount}."
                + (result.NumbersStoredAsText > 0 ? $" Чисел сохранено текстом для точности: {result.NumbersStoredAsText}." : "")
                + (result.WorksheetNameChanged ? $" Название листа адаптировано для Excel: «{result.WorksheetName}»." : "");
        }
        catch (Exception exception) { StatusText.Text = $"Не удалось экспортировать XLSX: {exception.Message}"; }
        finally { EndTransfer(); }
    }

    private async void OnPasteRows(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || !BeginTransfer()) return;
        try
        {
            var workspace = _workspace;
            var table = _currentTable;
            string? content = null;
            try { if (Clipboard is not null) content = await Clipboard.TryGetTextAsync(); }
            catch { StatusText.Text = "Буфер обмена недоступен. Вставьте ячейки в окно вручную."; }
            var preview = await new ClipboardPasteDialog(table, content).ShowDialog<ClipboardImportPreview?>(GetDialogOwner());
            if (preview is null || !await new ClipboardPreviewDialog(preview).ShowDialog<bool>(GetDialogOwner()))
            { StatusText.Text = "Вставка отменена. Данные не изменены."; return; }
            StatusText.Text = "Добавление строк…";
            var count = await Task.Run(() => preview.Commit(workspace));
            _offset = 0;
            RefreshRecords();
            StatusText.Text = $"Добавлено записей из Excel: {count}.";
        }
        catch (Exception exception) { StatusText.Text = $"Не удалось вставить строки: {exception.Message}"; }
        finally { EndTransfer(); }
    }
}
