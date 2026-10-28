using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Auth;

/// <summary>The allow-list, managed by the owner.</summary>
internal static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this IEndpointRouteBuilder api)
    {
        var invitations = api.MapGroup("/invitations").RequireAuthorization(AuthSetup.OwnerPolicy);

        invitations.MapGet("", ListAsync);
        invitations.MapPost("", CreateAsync);
        invitations.MapDelete("/{id:guid}", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var invitations = await db.Invitations.AsNoTracking().OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return Results.Ok(invitations.Select(InvitationResponse.From));
    }

    private static async Task<IResult> CreateAsync(CreateInvitationRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!IsEmail(request.Email))
        {
            return ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidEmail", "This is not an email address.");
        }

        var email = Invitation.NormalizeEmail(request.Email);
        if (await db.Invitations.AnyAsync(i => i.Email == email, ct))
        {
            return ApiProblem.Of(StatusCodes.Status409Conflict, "alreadyInvited", "This address is already invited.");
        }

        var invitation = new Invitation { Id = Guid.CreateVersion7(), Email = email, CreatedAt = clock.GetUtcNow() };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/invitations/{invitation.Id}", InvitationResponse.From(invitation));
    }

    /// <summary>Only pending invitations can be revoked; an accepted one belongs to an account.</summary>
    private static async Task<IResult> RevokeAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invitation is null)
        {
            return ApiProblem.Of(StatusCodes.Status404NotFound, "notFound", "There is no such invitation.");
        }

        if (invitation.UserId == User.OwnerId)
        {
            return ApiProblem.Of(StatusCodes.Status409Conflict, "ownerInvitation", "The owner's invitation comes from configuration.");
        }

        if (invitation.UserId is not null)
        {
            return ApiProblem.Of(StatusCodes.Status409Conflict, "invitationAccepted", "The invitation belongs to an account.");
        }

        db.Invitations.Remove(invitation);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool IsEmail([NotNullWhen(true)] string? value) =>
        value is { Length: > 0 and <= 320 }
        && MailAddress.TryCreate(value.Trim(), out var address)
        && address.Address == value.Trim()
        && address.Host.Contains('.', StringComparison.Ordinal);
}

internal sealed record CreateInvitationRequest(string? Email);

internal sealed record InvitationResponse(Guid Id, string Email, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt, bool IsOwner)
{
    public static InvitationResponse From(Invitation i) => new(i.Id, i.Email, i.CreatedAt, i.AcceptedAt, i.UserId == User.OwnerId);
}
