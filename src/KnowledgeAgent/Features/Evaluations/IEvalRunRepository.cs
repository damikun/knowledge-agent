namespace KnowledgeAgent.Features.Evaluations;

public interface IEvalRunRepository
{
    Task AddAsync(EvalRunRecord run, CancellationToken cancellationToken = default);
    Task<EvalRunRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvalRunRecord>> ListAsync(int take = 20, string? dataset = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvalRunRecord>> ListByStatusAsync(IReadOnlyCollection<string> statuses, CancellationToken cancellationToken = default);

    Task UpdateAsync(EvalRunRecord run, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
