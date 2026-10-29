namespace ExpenseInsights.Api;

internal static class ApiProblem
{
    /// <summary>A problem details response with a machine-readable <c>code</c> that the UI words through i18n.</summary>
    public static IResult Of(int status, string code, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
