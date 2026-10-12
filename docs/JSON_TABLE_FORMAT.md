# JSON table exchange

The `SmartSQL.Table.v1` format describes one table's fields and records. It is UTF-8 JSON and is independent of the storage provider.

Screenshots: [current application layout](smartsql-json-stage.jpg) and [JSON import preview](json-import-preview.jpg).

```json
{
  "format": "SmartSQL.Table.v1",
  "table": "Equipment",
  "fields": [
    { "name": "Name", "type": "Text" },
    { "name": "Price", "type": "Number" },
    { "name": "Purchased", "type": "Date" },
    { "name": "Available", "type": "Boolean" }
  ],
  "records": [
    ["Projector", 1250.50, "2026-10-12", true],
    ["Screen", null, null, false]
  ]
}
```

`format`, `fields`, and `records` are required. `table` is an optional informational label; the selected target table determines where import writes. Each record is an array in the order of the source `fields` array. Import matches names without case sensitivity and requires identical field types and a complete field set, so source field order may differ from the target table.

Supported type names are exactly `Text`, `Number`, `Date`, and `Boolean`. Export writes native JSON numbers and Booleans, ISO `YYYY-MM-DD` date strings, and unmodified nonempty text strings. Null represents an empty value. Empty or whitespace-only strings are rejected on import because the current storage model would otherwise convert them to null. Text that contains other characters retains surrounding spaces, newlines, and Unicode.

Numbers must fit .NET decimal exactly. Native JSON numbers may use an exponent, but overflow, underflow, and values that require rounding are rejected before writing. For compatibility, typed JSON strings may also use SmartSQL's ordinary inputs: decimal comma or period, `DD.MM.YYYY` or `YYYY-MM-DD` dates, and `Да`/`Нет`, `true`/`false`, or `1`/`0` Boolean strings. Export always emits native JSON number/Boolean values.

The desktop accepts files up to 50 MB; the parser also limits content to 50 million characters and 100,000 records. It accepts an optional UTF-8 BOM. Invalid JSON, duplicate or unknown properties, unsupported types, extra/missing values, and schema mismatches produce an error. No rows are written before preview confirmation. Commit checks the target schema again, appends all records transactionally, and prevents reusing the same preview.

Export includes all records of the selected table, including those outside the current page or active filters. Saved filters, relationships, record IDs, other tables, and database settings are outside this table format. Use a database backup when a complete local database copy is needed.
