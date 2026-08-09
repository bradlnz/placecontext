namespace PlaceContext.Application.Features;

/// <summary>
/// One occurrence of an identity-ish value in a project's data: this row of this table holds this
/// normalized value in this column. Rows in different tables sharing a NormalizedValue are linked.
/// Kind is one of: email | phone | address | name | url | text.
/// </summary>
public sealed record RecordLink(Guid ProjectId, string Kind, string NormalizedValue, string DisplayValue,
    string TableName, string ColumnName, string RowKey);
