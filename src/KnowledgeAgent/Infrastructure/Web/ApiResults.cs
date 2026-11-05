using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeAgent.Infrastructure.Web;

internal static class ApiResults
{
    public static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    public static ValidationProblem Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) => TypedResults.ValidationProblem(errors);
}
