using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseInsights.Testing;

namespace ExpenseInsights.Api.Tests;

public sealed class InvitationEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Uri Invitations = new("/api/invitations", UriKind.Relative);

    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task Owner_InvitesSomeone_WhoCanThenSignIn()
    {
        using var owner = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);
        var email = $"Friend-{Guid.NewGuid():N}@Example.test";
        var normalized = email.ToLowerInvariant();

        using var created = await owner.PostAsJsonAsync(Invitations, new { email = $"  {email} " }, CancellationToken);
        using var friend = await _factory.SignInAsync(normalized, CancellationToken);
        var list = await owner.GetFromJsonAsync<JsonElement>(Invitations, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitation = list.EnumerateArray().Single(i => i.GetProperty("email").GetString() == normalized);
        Assert.NotEqual(JsonValueKind.Null, invitation.GetProperty("acceptedAt").ValueKind);
        Assert.False(invitation.GetProperty("isOwner").GetBoolean());
        Assert.Contains(list.EnumerateArray(), i => i.GetProperty("email").GetString() == ApiFactory.OwnerEmail && i.GetProperty("isOwner").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Anna <anna@example.test>")]
    [InlineData("anna@localhost")]
    public async Task InvalidEmail_IsRejected(string email)
    {
        using var owner = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);

        using var response = await owner.PostAsJsonAsync(Invitations, new { email }, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalidEmail", await CodeAsync(response));
    }

    [Fact]
    public async Task SameEmailTwice_IsAConflict()
    {
        using var owner = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);
        var email = $"twice-{Guid.NewGuid():N}@example.test";

        using var first = await owner.PostAsJsonAsync(Invitations, new { email }, CancellationToken);
        using var second = await owner.PostAsJsonAsync(Invitations, new { email = email.ToUpperInvariant() }, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("alreadyInvited", await CodeAsync(second));
    }

    [Fact]
    public async Task PendingInvitation_CanBeRevoked_AcceptedAndOwnersCant()
    {
        using var owner = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);
        var pending = await Postgres.InviteAsync($"pending-{Guid.NewGuid():N}@example.test", CancellationToken);
        var acceptedEmail = $"accepted-{Guid.NewGuid():N}@example.test";
        var accepted = await Postgres.InviteAsync(acceptedEmail, CancellationToken);
        (await _factory.SignInAsync(acceptedEmail, CancellationToken)).Dispose();
        var list = await owner.GetFromJsonAsync<JsonElement>(Invitations, CancellationToken);
        var owners = list.EnumerateArray().Single(i => i.GetProperty("isOwner").GetBoolean()).GetProperty("id").GetGuid();

        using var revoked = await owner.DeleteAsync(new Uri($"/api/invitations/{pending}", UriKind.Relative), CancellationToken);
        using var again = await owner.DeleteAsync(new Uri($"/api/invitations/{pending}", UriKind.Relative), CancellationToken);
        using var ofAccount = await owner.DeleteAsync(new Uri($"/api/invitations/{accepted}", UriKind.Relative), CancellationToken);
        using var ofOwner = await owner.DeleteAsync(new Uri($"/api/invitations/{owners}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("invitationAccepted", await CodeAsync(ofAccount));
        Assert.Equal("ownerInvitation", await CodeAsync(ofOwner));
    }

    [Fact]
    public async Task RegularUsers_CantManageInvitations()
    {
        using var user = await _factory.SignInNewUserAsync(CancellationToken);
        var invitation = await Postgres.InviteAsync($"other-{Guid.NewGuid():N}@example.test", CancellationToken);

        using var list = await user.GetAsync(Invitations, CancellationToken);
        using var create = await user.PostAsJsonAsync(Invitations, new { email = "sneaky@example.test" }, CancellationToken);
        using var revoke = await user.DeleteAsync(new Uri($"/api/invitations/{invitation}", UriKind.Relative), CancellationToken);

        Assert.All([list, create, revoke], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
