using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Users;

namespace TicketFlow.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    public const string RoleClaim = "role";

    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(settings.Secret), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.FullName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [RoleClaim] = user.Role.ToString(),
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    /// <summary>512 bits from a CSPRNG, URL-safe encoded.</summary>
    public string GenerateRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64));

    /// <summary>
    /// Refresh tokens are high-entropy random values, so a fast unsalted hash is sufficient
    /// (unlike passwords) and allows an indexed lookup by hash.
    /// </summary>
    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public static SymmetricSecurityKey CreateSigningKey(string secret) => new(Encoding.UTF8.GetBytes(secret));
}
