using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ais.Core;

namespace Ais.Gateway;
public sealed class TokenDirectory
{
    readonly Dictionary<string, Actor> actors = new();
    public string Issue(Actor actor)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        actors[Fingerprint(token)] = actor;
        return token;
    }

    static string Fingerprint(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public Actor? Resolve(string token) => actors.GetValueOrDefault(Fingerprint(token));
}

public sealed class TokenAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TokenDirectory tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        var actor = tokens.Resolve(header[7..]);
        if (actor == null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actor.Id),
            new Claim(ClaimTypes.Role, actor.Role)
        };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(new ApiError("unauthorized", "Требуется действующий Bearer-токен"));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        await Response.WriteAsJsonAsync(new ApiError("access_denied", "Роль не разрешает операцию"));
    }
}
