namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectDataCsvImportDraft
{
    public string FileName = string.Empty;
    public string TableName = string.Empty;
    public bool HasHeader = true;
    public List<ProjectDataColumnDraft> Columns = [];
    public List<string[]> Records = [];
    public IEnumerable<string[]> DataRecords => HasHeader ? Records.Skip(1) : Records;
    public int DataRowCount => Math.Max(0, HasHeader ? Records.Count - 1 : Records.Count);

    public IEnumerable<string[]> PreviewRows() => DataRecords.Take(8);
}
