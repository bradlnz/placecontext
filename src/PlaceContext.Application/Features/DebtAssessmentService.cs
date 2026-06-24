using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Computes both debt dimensions for a project and stores an immutable assessment snapshot, applying
/// the scores to the project. Shared by the explicit recompute command and by project creation (a new
/// project is assessed immediately, from its code metrics, so it lands on the dashboards with real
/// debt). The caller owns the unit-of-work commit so the assessment is part of its transaction.
/// </summary>
public sealed class DebtAssessmentService
{
    private const int ReTouchWindow = 5;

    private readonly IChangeLedgerRepository _ledgers;
    private readonly IDebtAssessmentRepository _assessments;
    private readonly IDecisionTreeProvider _tree;
    private readonly ICodeMetricsProbe _codeMetrics;
    private readonly IDebtCalculatorFactory _factory;
    private readonly DebtScoreCalculator _calculator;
    private readonly IClock _clock;

    public DebtAssessmentService(
        IChangeLedgerRepository ledgers, IDebtAssessmentRepository assessments, IDecisionTreeProvider tree,
        ICodeMetricsProbe codeMetrics, IDebtCalculatorFactory factory, DebtScoreCalculator calculator, IClock clock)
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
    /// Probes graph + code metrics, runs the debt strategies, persists an assessment, and applies the
    /// scores to <paramref name="project"/>. Does not commit; the caller's unit of work does.
    /// </summary>
    public async Task<DebtAssessment> AssessAsync(Project project, CancellationToken ct = default)
    {
        var tree = await _tree.BuildAsync(project.Id, ct);
        var graphMetrics = tree.ToMetrics();
        var godNodes = tree.Hotspots();
        var codeMetrics = await _codeMetrics.ProbeAsync(project.Path, ct);
        var ledger = await _ledgers.GetForProjectAsync(project.Id, ct);

        var inputs = new DebtInputs(graphMetrics, codeMetrics, godNodes, ledger, ReTouchWindow);

        var signals = _factory.All().SelectMany(c => c.Compute(inputs)).ToList();
        var technical = _calculator.Calculate(signals.Where(s => s.Kind == DebtKind.Technical));
        var agentic = _calculator.Calculate(signals.Where(s => s.Kind == DebtKind.Agentic));

        var assessment = DebtAssessment.Create(project.Id, technical, agentic, signals, _clock.UtcNow);
        await _assessments.AddAsync(assessment, ct);

        project.ApplyDebt(technical, agentic, _clock.UtcNow);
        return assessment;
    }
}
