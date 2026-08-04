using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class DataMapViewModel
{
    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public bool Suggesting { get; private set; }
    public Guid? EditId { get; private set; }
    public Guid EdJobId { get; set; }
    public string EdTable { get; set; } = "";
    public string EdRowsPath { get; set; } = "";
    public bool EdEnabled { get; set; } = true;
    public string? EditorError { get; private set; }
    public List<FieldEdit> EdFields { get; } = new();

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void OpenEditor(DataMappingView? m)
    {
        EditId = m?.Id;
        EdJobId = m?.JobId ?? Guid.Empty;
        EdTable = m?.TargetTable ?? "";
        EdRowsPath = m?.RowsPath ?? "";
        EdEnabled = m?.Enabled ?? true;
        EdFields.Clear();
        if (m is not null)
            EdFields.AddRange(
                m.Fields.Select(f => new FieldEdit
                {
                    SourcePath = f.SourcePath,
                    Column = f.Column,
                    Type = f.Type,
                })
            );
        EditorError = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void OpenEditorForJob(JobView job)
    {
        OpenEditor(null);
        EdJobId = job.Id;
        EdTable = Sanitize(job.Name);
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        NotifyStateChanged();
    }

    public void AddField()
    {
        EdFields.Add(new FieldEdit());
        NotifyStateChanged();
    }

    public void RemoveField(int idx)
    {
        if (idx >= 0 && idx < EdFields.Count)
        {
            EdFields.RemoveAt(idx);
            NotifyStateChanged();
        }
    }

    public async Task SaveMappingAsync()
    {
        EditorError = null;
        if (EdJobId == Guid.Empty)
        {
            EditorError = "Pick a source job.";
            NotifyStateChanged();
            return;
        }
        if (string.IsNullOrWhiteSpace(EdTable))
        {
            EditorError = "Target table is required.";
            NotifyStateChanged();
            return;
        }
        var fields = EdFields
            .Where(f =>
                !string.IsNullOrWhiteSpace(f.SourcePath) || !string.IsNullOrWhiteSpace(f.Column)
            )
            .Select(f => new DataFieldDto(f.SourcePath.Trim(), f.Column.Trim(), f.Type))
            .ToList();
        if (fields.Count == 0)
        {
            EditorError = "Add at least one field.";
            NotifyStateChanged();
            return;
        }

        Saving = true;
        try
        {
            await _svc.SaveDataMappingAsync(
                new SaveDataMappingCommand(
                    ProjectId,
                    EdJobId,
                    EdTable.Trim(),
                    string.IsNullOrWhiteSpace(EdRowsPath) ? null : EdRowsPath.Trim(),
                    fields,
                    EdEnabled,
                    EditId,
                    SourceKind: "job"
                )
            );
            await LoadAsync();
            ShowEditor = false;
        }
        catch (Exception ex)
        {
            EditorError = ex.Message;
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteMappingAsync()
    {
        if (EditId is not { } id)
            return;
        Saving = true;
        try
        {
            await _svc.DeleteDataMappingAsync(id);
            await LoadAsync();
            ShowEditor = false;
        }
        catch (Exception ex)
        {
            EditorError = ex.Message;
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public async Task SuggestFieldsAsync()
    {
        if (EdJobId == Guid.Empty)
            return;
        Suggesting = true;
        EditorError = null;
        try
        {
            var runs = await _svc.ListJobRunsAsync(EdJobId);
            var last =
                runs.FirstOrDefault(r => r.Status is "Succeeded" or "Partial")
                ?? runs.FirstOrDefault();
            if (last is null)
            {
                EditorError = "This job has no runs yet — run it once, then suggest.";
                return;
            }
            var detail = await _svc.GetJobRunAsync(last.Id);
            var artifact =
                detail?.ReduceResult?.Artifact
                ?? detail
                    ?.ShardResults.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Artifact))
                    ?.Artifact;
            if (string.IsNullOrWhiteSpace(artifact))
            {
                EditorError = "The latest run has no result to sample.";
                return;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(artifact);
            var el = doc.RootElement;

            if (!string.IsNullOrWhiteSpace(EdRowsPath))
            {
                foreach (
                    var seg in EdRowsPath.Split(
                        '.',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                )
                {
                    if (
                        el.ValueKind != System.Text.Json.JsonValueKind.Object
                        || !el.TryGetProperty(seg, out el)
                    )
                    {
                        EditorError = $"Path '{EdRowsPath}' not found in the latest result.";
                        return;
                    }
                }
            }
            else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var firstArray = el.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Array);
                if (firstArray.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    EdRowsPath = firstArray.Name;
                    el = firstArray.Value;
                }
            }

            var sample =
                el.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? el.EnumerateArray().FirstOrDefault()
                    : el;
            if (sample.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                EditorError = "Couldn't find a record object to sample in the latest result.";
                return;
            }

            EdFields.Clear();
            foreach (var p in sample.EnumerateObject())
                EdFields.Add(
                    new FieldEdit
                    {
                        SourcePath = p.Name,
                        Column = Sanitize(p.Name),
                        Type = InferType(p.Value),
                    }
                );
        }
        catch (Exception ex)
        {
            EditorError = $"Couldn't sample the latest run: {ex.Message}";
        }
        finally
        {
            Suggesting = false;
            NotifyStateChanged();
        }
    }
}
