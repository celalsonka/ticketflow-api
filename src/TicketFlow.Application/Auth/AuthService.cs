using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Users;

namespace TicketFlow.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger)
{
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return UserErrors.EmailTaken;
        }

        var now = timeProvider.GetUtcNow();
        var user = User.Create(email, request.FullName, passwordHasher.Hash(request.Password), UserRole.Customer, now);
        db.Users.Add(user);
        var response = IssueTokens(user, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent registration with the same email (unique index).
            return UserErrors.EmailTaken;
        }

        logger.LogInformation("User {UserId} registered", user.Id);
        return response;
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Burn the same CPU time as a real verification so response timing does not reveal which emails exist.
            _ = passwordHasher.Hash(request.Password);
            return UserErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            logger.LogWarning("Failed login for user {UserId}", user.Id);
            return UserErrors.InvalidCredentials;
        }

        var response = IssueTokens(user, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return response;
    }

    /// <summary>
    /// Refresh-token rotation with reuse detection: every refresh token works exactly once.
    /// If an already-rotated token is presented again, it was most likely stolen, so every
    /// active session of that user is revoked.
    /// </summary>
    public async Task<Result<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var token = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
        {
            return UserErrors.InvalidRefreshToken;
        }

        if (token.IsRevoked)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", token.UserId);
            await RevokeAllAsync(token.UserId, now, "Reuse detected", ct);
            return UserErrors.InvalidRefreshToken;
        }

        if (!token.IsActive(now))
        {
            return UserErrors.InvalidRefreshToken;
        }

        var response = IssueTokens(token.User, now);
        token.Revoke(now, "Rotated", tokenService.HashRefreshToken(response.RefreshToken));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests raced to rotate the same token: only one may win.
            return UserErrors.InvalidRefreshToken;
        }

        return response;
    }

    public async Task<Result> LogoutAsync(Guid userId, RefreshTokenRequest request, CancellationToken ct)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);
        if (token is null)
        {
            return UserErrors.InvalidRefreshToken;
        }

        token.Revoke(timeProvider.GetUtcNow(), "Logged out");
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? UserErrors.NotFound : UserDto.From(user);
    }

    private AuthResponse IssueTokens(User user, DateTimeOffset now)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshToken = RefreshToken.Issue(
            user.Id,
            tokenService.HashRefreshToken(rawRefreshToken),
            now,
            TimeSpan.FromDays(jwtOptions.Value.RefreshTokenDays));

        db.RefreshTokens.Add(refreshToken);

        return new AuthResponse(accessToken.Token, accessToken.ExpiresAt, rawRefreshToken, refreshToken.ExpiresAt, UserDto.From(user));
    }

    private Task<int> RevokeAllAsync(Guid userId, DateTimeOffset now, string reason, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);
}
