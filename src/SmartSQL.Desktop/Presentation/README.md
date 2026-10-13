# Ресурсы интерфейса

`Strings.ru.axaml` — русские подписи общей оболочки. Используйте семантический
ключ через `DynamicResource`, например `Content="{DynamicResource UiTextImport}"`.
Подписи форматов CSV/JSON/XLSX и название SmartSQL сохраняют написание формата
и продукта. Тексты предметных данных не относятся к переводимым ресурсам.

Для будущего английского интерфейса добавьте `Strings.en.axaml` с тем же
набором ключей и переключайте один словарь приложения целиком. Не подменяйте
отдельные подписи английскими строками в представлениях. Переключатель языка
следует добавлять только вместе с переводом диалогов, сообщений об ошибках
и динамических подписей из C#: текущий словарь покрывает общую оболочку,
но пока не является полной локализацией приложения.

`Styles/Tokens.axaml` задаёт цвета по назначению, отступы и скругления.
`Styles/Controls.axaml` дополняет штатные шаблоны Fluent и сохраняет их
состояния клавиатурного фокуса и недоступности. Общие классы:

- `Border.card`, `Border.sidebar`, `Border.toolbar`;
- `Button.primary`, `Button.secondary`, `Button.quiet`, `Button.danger`;
- `Button.navigation` и дополнительный `active` для текущего раздела;
- `ListBox.navigation` для списка таблиц;
- `TextBlock.pageTitle`, `TextBlock.sectionTitle`, `TextBlock.muted`, `TextBlock.caption`;
- `Window.dialog` для белой поверхности диалогов.

Дополнительные элементы оболочки: `Button.iconButton`, `Button.navitem`
(`active` для выбранного раздела), `TextBlock.navsection`, `Menu.toolbarMenu`,
`MenuItem.toolbarMenuitem`, `Border.chip`, `Border.iconTile`, `DataGrid.flatgrid`.
Кнопки недоступных действий используют мягкую палитру без тяжёлых серых заливок;
их недоступность по-прежнему определяется `IsEnabled` и штатным поведением Fluent.

Цветовые ресурсы: `UiCanvasBrush`, `UiSurfaceBrush`, `UiSurfaceMutedBrush`,
`UiBorderBrush`, `UiTextBrush`, `UiMutedTextBrush`, `UiAccentBrush`,
`UiAccentHoverBrush`, `UiAccentSoftBrush`, `UiDangerBrush`, `UiFocusBrush`.
Также доступны `UiPaperBrush`, `UiSoftBorderBrush`, `UiDisabledTextBrush`,
`UiAccentDisabledBrush`. Основная палитра — тёплая светлая поверхность и тёмный
зелёный акцент; встроенный Inter применяется ко всем окнам.

Общие размеры: `UiPagePadding`, `UiCardPadding`, `UiButtonPadding`,
`UiCardRadius`, `UiControlRadius`, `UiSpaceSmall`, `UiSpaceMedium`, `UiSpaceLarge`.
Локальные размеры допустимы для конкретной компоновки; новые стили должны
использовать эти ресурсы, когда имеют такое же назначение.

`Styles/Icons.axaml` содержит нативные векторные `StreamGeometry`.
В XAML используйте `PathIcon Data="{DynamicResource UiIconTable}" Width="18" Height="18"`.
В C# — `UiIcon.Create("UiIconTable", 18)` из `SmartSQL.Desktop.Presentation`.
Иконка наследует цвет текста контейнера; для значимых действий сохраняйте
текстовую подпись или доступное имя и всплывающую подсказку у кнопки с одной иконкой.
Не используйте символы шрифтов или растровые изображения вместо этих ресурсов.

Ключи имеют префикс `UiIcon`: `Database` (`Db`), `Table`, `FolderOpen`, `Excel`,
`Filter`, `Sort`, `Search`, `Plus`, `Clipboard`, `Upload`, `Download`,
`ChevronLeft`, `ChevronRight`, `ChevronDown`, `Code`, `Clock`, `Check`,
`Structure`, `Help`, `Brand`, `Refresh`, `Lock`, `Copy`.
Для типов столбцов: `Text`, `Number`, `Calendar`, `Boolean`.
