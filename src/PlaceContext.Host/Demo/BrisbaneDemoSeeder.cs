using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.Extensions.Options;

namespace PlaceContext.Host.Demo;

/// <summary>
/// Seeds the "Brisbane property feasibility" demo through the platform's own surfaces — a project,
/// its Data-tab tables (suburb market stats, candidate development sites, feasibility scenarios),
/// decisions, work items, and project context — then queues the analytics chart sweep, so every
/// portal feature has something real to show. Idempotent: an already-seeded tenant just gets the
/// existing project back.
/// </summary>
public sealed class BrisbaneDemoSeeder
{
    public const string ProjectName = "brisbane-property-feasibility";

    private readonly IPlaceContextService _svc;
    private readonly AnalyticsRefreshQueue _charts;
    private readonly string _workspaceRoot;

    public BrisbaneDemoSeeder(IPlaceContextService svc, AnalyticsRefreshQueue charts, IOptions<PlaceContextOptions> options)
    {
        _svc = svc;
        _charts = charts;
        _workspaceRoot = options.Value.WorkspaceRoot;
    }

    public async Task<(Guid ProjectId, bool AlreadySeeded)> SeedAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        // A real working tree makes the project look like every other one in the workspace.
        var path = Path.Combine(_workspaceRoot, tenant.Slug, ProjectName);
        Directory.CreateDirectory(path);
        var readme = Path.Combine(path, "README.md");
        if (!File.Exists(readme))
            await File.WriteAllTextAsync(readme,
                "# Brisbane property feasibility\n\nDemo project seeded by PlaceContext: suburb market data, " +
                "candidate development sites, and feasibility scenarios live in the project database (Data tab); " +
                "Analytics draws the charts.\n", ct);

        var project = await _svc.CreateProjectAsync(path, ProjectName, ct);

        var tables = await _svc.ListProjectDataTablesAsync(project.Id, ct);
        if (tables.Any(t => t.Name == "suburbs"))
        {
            // Already seeded — don't duplicate rows, but do requeue the chart sweep so re-seeding
            // is the supported way to redraw the demo's Analytics (e.g. after a renderer upgrade).
            _charts.TryEnqueue(tenant, project.Id, ProjectName);
            return (project.Id, true);
        }

        await _svc.ExecuteProjectDataAsync(project.Id, SuburbsSql, ct);
        await _svc.ExecuteProjectDataAsync(project.Id, SitesSql, ct);
        await _svc.ExecuteProjectDataAsync(project.Id, FeasibilitySql, ct);

        await _svc.AddDecisionAsync(project.Id,
            "Which market segment do we target?",
            "Middle-ring infill townhouses and small apartment sites (5–20 km from CBD)",
            "Inner-ring pricing compresses margins below 15%; outer-ring depth is thin. Middle-ring suburbs " +
            "combine >3% growth, sub-1.5% vacancy, and land still under $2,200/sqm.", ct);
        await _svc.AddDecisionAsync(project.Id,
            "What is the go/no-go feasibility threshold?",
            "Development margin ≥ 18% on gross realisation, with a 12% sensitivity floor",
            "18% covers finance + contingency at current build-cost escalation (~6%/yr); scenarios below the " +
            "12% floor after a 10% cost shock are rejected outright.", ct);
        await _svc.AddDecisionAsync(project.Id,
            "How do we rank candidate sites?",
            "Residual land value per allowable unit, then vacancy-adjusted rental yield as tiebreaker",
            "RLV/unit normalises across zonings (LMR vs MDR); the yield tiebreaker favours suburbs where " +
            "unsold stock can be held and rented without bleeding.", ct);

        await _svc.AddWorkItemAsync(project.Id, "Shortlist top 3 sites by residual land value",
            "Run the feasibility table ranking (Data tab) and confirm against the decision threshold (≥18% margin).", "High", ct);
        await _svc.AddWorkItemAsync(project.Id, "Commission DA pre-lodgement advice for Woolloongabba site",
            "Zoning MDR, 1,214 sqm — the Cross River Rail catchment uplift makes this the highest-RLV candidate.", "High", ct);
        await _svc.AddWorkItemAsync(project.Id, "Refresh suburb stats after the next ABS/CoreLogic release",
            "Re-run the Analytics sweep afterwards so the charts pick up the new medians.", "Normal", ct);
        await _svc.AddWorkItemAsync(project.Id, "Model a 10% build-cost shock across all scenarios",
            "Stress the feasibility table (margin_pct column) and flag any scenario falling under the 12% floor.", "Normal", ct);

        await _svc.SetContextAsync(project.Id, ContextMarkdown, ct);

        // Draw the charts in the background — the demo lands with Analytics already populated.
        _charts.TryEnqueue(tenant, project.Id, ProjectName);

        return (project.Id, false);
    }

    private const string SuburbsSql = """
        CREATE TABLE suburbs (
          name text NOT NULL, region text NOT NULL, distance_cbd_km numeric NOT NULL,
          median_house_price integer NOT NULL, median_unit_price integer NOT NULL,
          growth_12m_pct numeric NOT NULL, rental_yield_pct numeric NOT NULL, vacancy_pct numeric NOT NULL);
        INSERT INTO suburbs VALUES
          ('New Farm','Inner North',2.0,2650000,780000,4.1,2.8,1.1),
          ('Teneriffe','Inner North',2.5,2480000,745000,3.8,2.9,1.2),
          ('Newstead','Inner North',3.0,1950000,720000,4.4,3.4,1.4),
          ('Hamilton','Inner North',6.0,2100000,655000,3.6,3.2,1.3),
          ('Ascot','Inner North',6.5,2250000,610000,3.2,3.0,1.2),
          ('Nundah','Middle North',8.5,1120000,545000,5.2,4.1,0.9),
          ('Chermside','Middle North',9.5,985000,498000,5.8,4.4,0.8),
          ('Paddington','Inner West',3.0,1980000,690000,4.0,3.1,1.0),
          ('Toowong','Inner West',4.5,1540000,600000,4.6,3.6,1.1),
          ('Indooroopilly','Middle West',7.0,1490000,585000,4.3,3.7,1.0),
          ('St Lucia','Inner West',5.0,1680000,575000,3.9,3.5,1.3),
          ('Kelvin Grove','Inner North',3.5,1420000,565000,4.7,3.8,1.0),
          ('West End','Inner South',2.5,1720000,700000,4.5,3.4,1.5),
          ('Woolloongabba','Inner South',3.0,1380000,640000,6.1,3.9,1.2),
          ('Kangaroo Point','Inner South',2.0,1560000,685000,4.8,3.6,1.6),
          ('Coorparoo','Middle South',5.5,1350000,560000,5.0,3.9,0.9),
          ('Annerley','Middle South',5.0,1210000,520000,5.4,4.2,0.8),
          ('Moorooka','Middle South',7.0,1030000,470000,5.9,4.5,0.7),
          ('Mount Gravatt','Middle South',10.0,1080000,505000,5.5,4.3,0.8),
          ('Sunnybank','Outer South',13.5,1240000,530000,4.9,4.0,0.9),
          ('Carindale','Middle East',10.5,1310000,545000,4.4,3.8,0.8),
          ('Morningside','Middle East',6.0,1190000,535000,5.1,4.0,0.9),
          ('Wynnum','Bayside',14.5,1010000,565000,6.4,4.4,0.7),
          ('Manly','Bayside',15.5,1160000,610000,5.7,4.1,0.8);
        """;

    private const string SitesSql = """
        CREATE TABLE sites (
          id integer NOT NULL PRIMARY KEY, address text NOT NULL, suburb text NOT NULL,
          land_sqm integer NOT NULL, zoning text NOT NULL, asking_price integer NOT NULL,
          allowable_units integer NOT NULL);
        INSERT INTO sites VALUES
          (1,'18 Latrobe St','Woolloongabba',1214,'MDR (3 storey)',3900000,18),
          (2,'44 Wilkie St','Annerley',812,'LMR (2-3 storey)',1950000,9),
          (3,'9 Palm Ave','Nundah',1012,'LMR (2-3 storey)',2350000,12),
          (4,'126 Junction Rd','Morningside',930,'LMR (2 storey)',1780000,8),
          (5,'7 Beaconsfield Tce','Wynnum',1520,'MDR (3 storey)',2600000,16),
          (6,'233 Gympie Rd','Chermside',1418,'MDR (5 storey)',3450000,26),
          (7,'12 Ferry Rd','West End',890,'MDR (4 storey)',4200000,15),
          (8,'85 Cavendish Rd','Coorparoo',760,'LMR (2-3 storey)',1690000,8),
          (9,'31 Station Ave','Moorooka',1105,'LMR (2-3 storey)',1520000,11),
          (10,'5 Riding Rd','Kangaroo Point',680,'MDR (5 storey)',3800000,14);
        """;

    private const string FeasibilitySql = """
        CREATE TABLE feasibility (
          site_id integer NOT NULL, scenario text NOT NULL, units integer NOT NULL,
          gfa_sqm integer NOT NULL, build_cost_total integer NOT NULL, total_dev_cost integer NOT NULL,
          gross_realisation integer NOT NULL, profit integer NOT NULL, margin_pct numeric NOT NULL,
          rlv_per_unit integer NOT NULL);
        INSERT INTO feasibility VALUES
          (1,'townhouse-18',18,2450,8330000,13720000,17100000,3380000,19.8,216000),
          (1,'apartment-24',24,2900,10440000,16480000,19920000,3440000,17.3,178000),
          (2,'townhouse-9',9,1240,4220000,6890000,8550000,1660000,19.4,201000),
          (3,'townhouse-12',12,1660,5640000,9040000,11160000,2120000,19.0,196000),
          (4,'townhouse-8',8,1090,3710000,6120000,7440000,1320000,17.7,182000),
          (5,'townhouse-16',16,2180,7410000,11290000,13760000,2470000,17.9,168000),
          (6,'apartment-26',26,3120,11230000,16130000,19760000,3630000,18.4,152000),
          (6,'apartment-32-bonus',32,3720,13390000,18450000,23040000,4590000,19.9,161000),
          (7,'apartment-15',15,1980,7130000,12490000,14400000,1910000,13.3,141000),
          (8,'townhouse-8',8,1080,3670000,5920000,7280000,1360000,18.7,189000),
          (9,'townhouse-11',11,1500,5100000,7280000,9130000,1850000,20.3,211000),
          (10,'apartment-14',14,1820,6550000,11430000,13160000,1730000,13.1,138000);
        """;

    private const string ContextMarkdown = """
        # Brisbane property feasibility — analysis context

        We are screening middle-ring Brisbane development sites for townhouse/small-apartment
        projects. The project database (Data tab) holds three tables:

        - **suburbs** — market stats per suburb: medians, 12-month growth, rental yield, vacancy.
        - **sites** — candidate acquisitions with zoning and allowable yield.
        - **feasibility** — costed scenarios per site: total development cost, gross realisation,
          profit, development margin, and residual land value per unit.

        Ground rules (see Decisions): target middle-ring infill, go/no-go at ≥18% development
        margin (12% floor under a 10% cost shock), rank by RLV per allowable unit. The Analytics
        tab charts these tables; redraw any chart with a steering instruction (e.g. "margin_pct by
        suburb, highlight the 18% threshold").
        """;
}
