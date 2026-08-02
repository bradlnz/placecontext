using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class DataMapViewModel
{
    // ── Canvas state ──────────────────────────────────────────────────────────────────────────
    public Dictionary<string, (double X, double Y)> Pos { get; } = new();
    public bool PosLoaded { get; private set; }
    public string? DragKey { get; set; }
    public double PanX { get; set; }
    public double PanY { get; set; }
    public bool Panning { get; set; }
    public string? ConnectJobId { get; set; }
    public JobView? ConnectJob { get; set; }
    public (double X, double Y) ConnectEnd { get; set; }
    public double LastX { get; set; }
    public double LastY { get; set; }
    public bool Moved { get; set; }

    public static readonly string[] ColumnTypes = DataColumnTypes.All.ToArray();

    // ── Canvas drag ───────────────────────────────────────────────────────────────────────────
    public void StartDrag(string key, double clientX, double clientY)
    {
        DragKey = key;
        LastX = clientX;
        LastY = clientY;
        Moved = false;
    }

    public void OnCanvasDown(double clientX, double clientY)
    {
        Panning = true;
        LastX = clientX;
        LastY = clientY;
    }

    public void StartConnect(JobView job, double clientX, double clientY)
    {
        ConnectJob = job;
        ConnectJobId = "job:" + job.Id;
        var p = GetPos(ConnectJobId);
        ConnectEnd = (p.X + 190, p.Y + 22);
        LastX = clientX;
        LastY = clientY;
    }


    public void OnCanvasMove(double clientX, double clientY)
    {
        var dx = clientX - LastX;
        var dy = clientY - LastY;
        if (DragKey is { } key)
        {
            var p = GetPos(key);
            Pos[key] = (Math.Max(0, p.X + dx), Math.Max(0, p.Y + dy));
            if (Math.Abs(dx) + Math.Abs(dy) > 1) Moved = true;
        }
        else if (ConnectJob is not null)
        {
            ConnectEnd = (ConnectEnd.X + dx, ConnectEnd.Y + dy);
        }
        else if (Panning)
        {
            PanX += dx;
            PanY += dy;
        }
        else return;
        LastX = clientX;
        LastY = clientY;
    }

    public async Task OnCanvasUpAsync()
    {
        if (Panning) { Panning = false; return; }
        if (DragKey is not null)
        {
            var clickedJob = !Moved && DragKey.StartsWith("job:") ? DragKey["job:".Length..] : null;
            DragKey = null;
            await SavePositionsAsync();
            if (clickedJob is not null && Guid.TryParse(clickedJob, out var jobId))
            {
                if (Mappings?.FirstOrDefault(m => m.JobId == jobId) is { } existing) OpenEditor(existing);
                else if (Jobs?.FirstOrDefault(j => j.Id == jobId) is { } job) OpenEditorForJob(job);
            }
        }
        else if (ConnectJob is { } job)
        {
            ConnectJob = null;
            OpenEditorForJob(job);
        }
    }

    public async Task OnTableUpAsync(string table)
    {
        if (ConnectJob is { } job)
        {
            ConnectJob = null;
            OpenEditorForJob(job);
            EdTable = table;
            return;
        }
        if (DragKey is not null)
        {
            DragKey = null;
            await SavePositionsAsync();
        }
    }

    // ── Edge paths ────────────────────────────────────────────────────────────────────────────
    public string EdgePath(DataMappingView m)
    {
        var from = GetPos("job:" + m.JobId);
        var to = GetPos("table:" + m.TargetTable);
        var x1 = from.X + 190 + PanX; var y1 = from.Y + 26 + PanY;
        var x2 = to.X + PanX; var y2 = to.Y + 26 + PanY;
        var bend = Math.Max(40, (x2 - x1) / 2);
        return FormattableString.Invariant($"M {x1} {y1} C {x1 + bend} {y1}, {x2 - bend} {y2}, {x2} {y2}");
    }

    public string ConnectPath()
    {
        if (ConnectJobId is null) return "";
        var from = GetPos(ConnectJobId);
        var x1 = from.X + 190 + PanX; var y1 = from.Y + 22 + PanY;
        return FormattableString.Invariant($"M {x1} {y1} L {ConnectEnd.X + PanX} {ConnectEnd.Y + PanY}");
    }

}
