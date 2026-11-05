using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;

namespace KnowledgeAgent.Features.Labels;

public interface ILabelService
{
    Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<LabelDto> CreateAsync(CreateLabelRequest request, CancellationToken cancellationToken = default);

    Task<LabelDto?> UpdateAsync(Guid id, UpdateLabelRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationLabelDto>?> SetConversationLabelsAsync(Guid conversationId, IReadOnlyList<Guid>? labelIds, CancellationToken cancellationToken = default);

    Task<int> RelabelAsync(int days, CancellationToken cancellationToken = default);
}

internal sealed class LabelService(ILabelRepository labels, IJobQueue jobs, TimeProvider timeProvider) : ILabelService
{
    public const int MaxLabels = 100;
    public const int MaxLabelsPerConversation = 20;
    public const int MaxRelabelDays = 90;

    // A re-label costs a model call per conversation, so one request queues at most this many.
    public const int MaxRelabel = 500;

    public Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken cancellationToken = default) => labels.ListAsync(cancellationToken);

    public async Task<LabelDto> CreateAsync(CreateLabelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = ValidName(request.Name, errors);
        var color = request.Color ?? LabelColors.Gray;
        ValidateColor(color, errors);
        var description = ValidDescription(request.Description, errors);
        ThrowIfAny(errors);

        if ((await labels.ListAsync(cancellationToken)).Count >= MaxLabels)
        {
            throw new ValidationFailedException("name", $"There can be at most {MaxLabels} labels.");
        }

        await EnsureNameFreeAsync(name, exceptId: null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var label = new Label
        {
            Name = name,
            Color = color,
            Description = description,
            AutoApply = request.AutoApply ?? false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        try
        {
            await labels.AddAsync(label, cancellationToken);
        }
        catch (LabelNameTakenException)
        {
            throw NameTaken(name);
        }

        return ToDto(label, conversationCount: 0);
    }

    public async Task<LabelDto?> UpdateAsync(Guid id, UpdateLabelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var label = await labels.GetAsync(id, cancellationToken);
        if (label is null)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = request.Name is null ? label.Name : ValidName(request.Name, errors);
        if (request.Color is not null)
        {
            ValidateColor(request.Color, errors);
        }

        var description = request.Description is null ? label.Description : ValidDescription(request.Description, errors);
        ThrowIfAny(errors);

        if (!string.Equals(Label.Normalize(name), label.NormalizedName, StringComparison.Ordinal))
        {
            await EnsureNameFreeAsync(name, id, cancellationToken);
        }

        label.Name = name;
        label.Color = request.Color ?? label.Color;
        label.Description = description;
        label.AutoApply = request.AutoApply ?? label.AutoApply;
        label.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            await labels.UpdateAsync(label, cancellationToken);
        }
        catch (LabelNameTakenException)
        {
            throw NameTaken(name);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        return (await labels.ListAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => labels.DeleteAsync(id, cancellationToken);

    public async Task<IReadOnlyList<ConversationLabelDto>?> SetConversationLabelsAsync(
        Guid conversationId,
        IReadOnlyList<Guid>? labelIds,
        CancellationToken cancellationToken = default)
    {
        if (labelIds is null)
        {
            throw new ValidationFailedException("labelIds", "labelIds is required; send an empty list to take every label off.");
        }

        var wanted = labelIds.Distinct().ToList();
        if (wanted.Count > MaxLabelsPerConversation)
        {
            throw new ValidationFailedException("labelIds", $"A conversation can have at most {MaxLabelsPerConversation} labels.");
        }

        var known = (await labels.ListByIdsAsync(wanted, cancellationToken)).Select(label => label.Id).ToHashSet();
        var unknown = wanted.Where(id => !known.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationFailedException("labelIds", $"These labels do not exist: {string.Join(", ", unknown)}.");
        }

        try
        {
            return await labels.SetAsync(conversationId, wanted, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public async Task<int> RelabelAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > MaxRelabelDays)
        {
            throw new ValidationFailedException("days", $"days must be 1 to {MaxRelabelDays}.");
        }

        if ((await labels.ListAutoApplyAsync(cancellationToken)).Count == 0)
        {
            throw new ValidationFailedException("days", "No label is set to apply automatically, so there is nothing to re-label with.");
        }

        var ids = await labels.ResetAutoLabeledAsync(timeProvider.GetUtcNow().AddDays(-days), MaxRelabel, cancellationToken);
        foreach (var id in ids)
        {
            await jobs.EnqueueAsync(AutoLabelJob.Kind, id.ToString(), cancellationToken: cancellationToken);
        }

        return ids.Count;
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await labels.NameTakenAsync(name, exceptId, cancellationToken))
        {
            throw NameTaken(name);
        }
    }

    private static ValidationFailedException NameTaken(string name) => new("name", $"A label named '{name}' already exists.");

    private static string ValidName(string? name, Dictionary<string, string[]> errors)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > Label.MaxNameLength)
        {
            errors["name"] = [$"name must be 1 to {Label.MaxNameLength} characters."];
        }

        return trimmed;
    }

    private static void ValidateColor(string color, Dictionary<string, string[]> errors)
    {
        if (!LabelColors.IsValid(color))
        {
            errors["color"] = [$"color must be one of {string.Join(", ", LabelColors.All)}."];
        }
    }

    private static string ValidDescription(string? description, Dictionary<string, string[]> errors)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        if (trimmed.Length > Label.MaxDescriptionLength)
        {
            errors["description"] = [$"description must be at most {Label.MaxDescriptionLength} characters."];
        }

        return trimmed;
    }

    private static void ThrowIfAny(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }
    }

    private static LabelDto ToDto(Label label, int conversationCount) =>
        new(label.Id, label.Name, label.Color, label.Description, label.AutoApply, conversationCount, label.CreatedAt, label.UpdatedAt);
}
