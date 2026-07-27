using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    // ── SQL editor ────────────────────────────────────────────────────────────────────────────
    public ProjectQueryResult? Result { get; private set; }
    public string? Error { get; private set; }
    public bool Running { get; private set; }
    public bool MonacoReady { get; set; }
    public bool MonacoLite { get; set; }
    public const string EditorId = "pcdata-editor";
    public const string StarterSql =
        "-- This project's own database. Standard SQL — a few ideas:\n" +
        "--   CREATE TABLE readings (at timestamptz DEFAULT now(), sensor text, value numeric);\n" +
        "--   INSERT INTO readings (sensor, value) VALUES ('door', 21.5);\n" +
        "--   SELECT sensor, avg(value) FROM readings GROUP BY sensor;\n\n";

}
