using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Users;
using TicketFlow.Infrastructure.Security;

namespace TicketFlow.UnitTests.Infrastructure;

public class SecurityTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "issuer",
        Audience = "audience",
        Secret = "unit-test-secret-0123456789-abcdefghijklmnopqrstuvwxyz",
        AccessTokenMinutes = 15,
    };

    [Fact]
    public async Task Access_token_is_signed_and_carries_identity_and_role()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), clock);
        var user = User.Create("Jane@Example.com ", "Jane Doe", "hash", UserRole.Organizer, clock.GetUtcNow());

        var token = service.CreateAccessToken(user);

        Assert.Equal(clock.GetUtcNow().AddMinutes(15), token.ExpiresAt);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = "issuer",
            ValidAudience = "audience",
            IssuerSigningKey = JwtTokenService.CreateSigningKey(Options.Secret),
            ValidateLifetime = false,
        });

        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal(user.Id.ToString(), validation.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("jane@example.com", validation.Claims[JwtRegisteredClaimNames.Email]);
        Assert.Equal("Organizer", validation.Claims["role"]);
    }

    [Fact]
    public void Refresh_tokens_are_random_and_only_their_hash_is_deterministic()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

        var first = service.GenerateRefreshToken();
        var second = service.GenerateRefreshToken();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 80);
        Assert.Equal(service.HashRefreshToken(first), service.HashRefreshToken(first));
        Assert.NotEqual(first, service.HashRefreshToken(first));
    }

    [Fact]
    public void Password_hasher_verifies_only_the_original_password()
    {
        var hasher = new IdentityPasswordHasher();

        var hash = hasher.Hash("Passw0rd!");

        Assert.NotEqual("Passw0rd!", hash);
        Assert.True(hasher.Verify(hash, "Passw0rd!"));
        Assert.False(hasher.Verify(hash, "passw0rd!"));
        Assert.NotEqual(hash, hasher.Hash("Passw0rd!")); // salted
    }
}
