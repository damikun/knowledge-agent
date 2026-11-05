using Microsoft.AspNetCore.Http.Metadata;

namespace KnowledgeAgent.Infrastructure.Web;

internal sealed record RequestBodySizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;
