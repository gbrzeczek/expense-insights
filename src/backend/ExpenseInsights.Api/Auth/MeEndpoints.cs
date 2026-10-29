using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Auth;

internal static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me");

        me.MapGet("", GetAsync);
        me.MapDelete("", DeleteAsync);
    }

    private static async Task<IResult> GetAsync(AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == db.CurrentUserId, ct);
        return Results.Ok(new MeResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }

    /// <summary>Deletes the account and all its data, then ends the session.</summary>
    private static async Task<IResult> DeleteAsync(HttpContext context, AppDbContext db, AccountDeletion deletion, CancellationToken ct)
    {
        if (db.CurrentUserId == User.OwnerId)
        {
            return ApiProblem.Of(StatusCodes.Status409Conflict, "ownerCannotBeDeleted", "The owner's account can't be deleted.");
        }

        await deletion.DeleteCurrentUserAsync(ct);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Xsrf.Clear(context);
        return Results.NoContent();
    }
}

internal sealed record MeResponse(Guid Id, string? Email, string? DisplayName, UserRole Role);
