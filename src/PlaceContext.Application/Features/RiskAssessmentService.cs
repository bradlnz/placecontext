using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Computes both risk dimensions for a project and stores an immutable assessment snapshot, applying
/// the scores to the project. Shared by the explicit recompute command and by project creation (a new
/// project is assessed immediately, from its code metrics, so it lands on the dashboards with real
/// risk). The caller owns the unit-of-work commit so the assessment is part of its transaction.
/// </summary>
public sealed class RiskAssessmentService
{
    private const int ReTouchWindow = 5;

    private readonly IActivityLogRepository _ledgers;
    private readonly IRiskAssessmentRepository _assessments;
    private readonly IDecisionTreeProvider _tree;
    private readonly ICodeMetricsProbe _codeMetrics;
    private readonly IRiskCalculatorFactory _factory;
    private readonly RiskScoreCalculator _calculator;
    private readonly IClock _clock;

    public RiskAssessmentService(
        IActivityLogRepository ledgers, IRiskAssessmentRepository assessments, IDecisionTreeProvider tree,
        ICodeMetricsProbe codeMetrics, IRiskCalculatorFactory factory, RiskScoreCalculator calculator, IClock clock)
    {
        _ledgers = ledgers;
        _assessments = assessments;
        _tree = tree;
        _codeMetrics = codeMetrics;
        _factory = factory;
        _calculator = calculator;
        _clock = clock;
    }

    /// <summary>
    /// Probes graph + code metrics, runs the risk strategies, persists an assessment, and applies the
    /// scores to <paramref name="project"/>. Does not commit; the caller's unit of work does.
    /// </summary>
    public async Task<RiskAssessment> AssessAsync(Project project, CancellationToken ct = default)
    {
        var tree = await _tree.BuildAsync(project.Id, ct);
        var graphMetrics = tree.ToMetrics();
        var godNodes = tree.Hotspots();
        var codeMetrics = await _codeMetrics.ProbeAsync(project.Path, ct);
        var ledger = await _ledgers.GetForProjectAsync(project.Id, ct);

        var inputs = new RiskInputs(graphMetrics, codeMetrics, godNodes, ledger, ReTouchWindow);

        var signals = _factory.All().SelectMany(c => c.Compute(inputs)).ToList();
        var technical = _calculator.Calculate(signals.Where(s => s.Kind == RiskKind.Technical));
        var process = _calculator.Calculate(signals.Where(s => s.Kind == RiskKind.Process));

        var assessment = RiskAssessment.Create(project.Id, technical, process, signals, _clock.UtcNow);
        await _assessments.AddAsync(assessment, ct);

        project.ApplyRisk(technical, process, _clock.UtcNow);
        return assessment;
    }
}
