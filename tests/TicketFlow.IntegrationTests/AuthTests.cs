using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TicketFlow.Application.Auth;
using TicketFlow.Domain.Users;
using TicketFlow.IntegrationTests.Infrastructure;

namespace TicketFlow.IntegrationTests;

public class AuthTests(TicketFlowApiFactory factory) : ApiTestBase(factory), IClassFixture<TicketFlowApiFactory>
{
    [Fact]
    public async Task Register_returns_tokens_and_the_access_token_works()
    {
        var client = Anonymous();
        var email = UniqueEmail();

        var auth = await ReadAsync<AuthResponse>(
            await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email.ToUpperInvariant(), Password, "Jane Doe"), Json),
            HttpStatusCode.Created);

        Assert.Equal(email, auth.User.Email); // normalised
        Assert.Equal(UserRole.Customer, auth.User.Role);
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await ReadAsync<UserDto>(await client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(auth.User.Id, me.Id);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_409()
    {
        var client = Anonymous();
        var request = new RegisterRequest(UniqueEmail(), Password, "Jane Doe");
        await client.PostAsJsonAsync("/api/v1/auth/register", request, Json);

        var duplicate = await client.PostAsJsonAsync("/api/v1/auth/register", request, Json);

        Assert.Equal("User.EmailTaken", await ProblemCodeAsync(duplicate, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Register_with_weak_password_returns_validation_problem()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("not-an-email", "short", ""), Json);

        Assert.Equal("Validation.Failed", await ProblemCodeAsync(response, HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Password\"", body, StringComparison.Ordinal);
        Assert.Contains("\"Email\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_wrong_password_and_unknown_email_look_identical()
    {
        var (_, auth) = await SignInAsync();
        var client = Anonymous();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(auth.User.Email, "Wrong-Passw0rd"), Json);
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(UniqueEmail(), Password), Json);

        Assert.Equal("Auth.InvalidCredentials", await ProblemCodeAsync(wrongPassword, HttpStatusCode.Unauthorized));
        Assert.Equal("Auth.InvalidCredentials", await ProblemCodeAsync(unknownEmail, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_reuse_of_an_old_token_revokes_the_whole_session_family()
    {
        var (_, auth) = await SignInAsync();
        var client = Anonymous();

        var rotated = await ReadAsync<AuthResponse>(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(auth.RefreshToken), Json));
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);

        // An attacker replays the stolen, already-rotated token...
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(auth.RefreshToken), Json);
        Assert.Equal("Auth.InvalidRefreshToken", await ProblemCodeAsync(replay, HttpStatusCode.Unauthorized));

        // ...which also kills the legitimate user's current token, forcing a fresh login.
        var legit = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(rotated.RefreshToken), Json);
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (client, auth) = await SignInAsync();

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequest(auth.RefreshToken), Json);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(auth.RefreshToken), Json);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        var client = Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/orders")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/orders")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_manage_users()
    {
        var (customer, customerAuth) = await SignInAsync();
        var (admin, _) = await SignInAsync(UserRole.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/users")).StatusCode);

        var promoted = await ReadAsync<UserDto>(await admin.PutAsJsonAsync(
            $"/api/v1/users/{customerAuth.User.Id}/role", new Application.Users.ChangeRoleRequest(UserRole.Organizer), Json));
        Assert.Equal(UserRole.Organizer, promoted.Role);
    }
}
