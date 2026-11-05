namespace KnowledgeAgent.Features.Evaluations;

public interface IEvalService
{
    Task<EvalDatasetListDto> ListDatasetsAsync(CancellationToken cancellationToken = default);

    Task<EvalDatasetSyncResultDto?> SyncDatasetAsync(string name, CancellationToken cancellationToken = default);

    Task<EvalRunSummaryDto> StartRunAsync(StartEvalRunRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvalRunSummaryDto>> ListRunsAsync(int limit = 20, string? dataset = null, CancellationToken cancellationToken = default);

    Task<EvalRunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EvalRunSummaryDto?> CancelRunAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EvalRunDeleteResult> DeleteRunAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum EvalRunDeleteResult
{
    Deleted,
    NotFound,
    Active,
}
