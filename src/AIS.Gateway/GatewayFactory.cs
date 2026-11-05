using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ais.Core;

namespace Ais.Gateway;
public static class GatewayFactory
{
    public static WebApplication Create(IPatientRepository repository, IAuditStore audit, TokenDirectory tokens, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(tokens);
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TokenAuthentication>("Bearer", _ =>
        {
        });
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var requests = new Dictionary<Guid, (string Actor, AccessRequest Request, AccessReply Reply)>();
        var gate = new object ();
        Actor ActorFor(HttpContext c) => tokens.Resolve(c.Request.Headers.Authorization.ToString()[7..])!;
        IResult Error(int status, string code, string message) => Results.Json(new ApiError(code, message), statusCode: status);
        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        app.MapGet("/api/v1/patients/{id:guid}/summary", (Guid id, HttpContext context) =>
        {
            var actor = ActorFor(context);
            if (!AccessPolicy.CanRead(actor, id))
            {
                audit.Write(actor.Id, id, "read_summary", "denied");
                return Error(403, "access_denied", "Нет доступа к карте");
            }

            var patient = repository.Find(id);
            if (patient == null)
                return Error(404, "not_found", "Карта не найдена");
            audit.Write(actor.Id, id, "read_summary", "allowed");
            return Results.Ok(new PatientSummary(patient.Id, patient.FullName, patient.BirthDate.ToString("yyyy-MM-dd"), patient.Sex, "Учебная запись без медицинского содержания"));
        }).RequireAuthorization();
        app.MapPost("/api/v1/access/check", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType())
                return Error(415, "unsupported_media_type", "Нужен Content-Type application/json");
            AccessRequest? parsed;
            try
            {
                parsed = await context.Request.ReadFromJsonAsync<AccessRequest>();
            }
            catch (JsonException)
            {
                return Error(400, "invalid_json", "Неверный JSON или тип поля");
            }
            catch (BadHttpRequestException)
            {
                return Error(400, "invalid_json", "Невозможно прочитать запрос");
            }

            if (parsed == null)
                return Error(400, "invalid_json", "Тело запроса не должно быть null");
            AccessRequest request = parsed;
            var errors = AccessPolicy.Validate(request);
            if (errors.Count > 0)
                return Results.Json(new ApiError("validation_error", "Некорректные поля", errors), statusCode: 400);
            var actor = ActorFor(context);
            if (!AccessPolicy.CanCheck(actor, request))
            {
                audit.Write(actor.Id, request.PatientId, "check_access", "denied");
                return Error(403, "access_denied", "Роль, назначение или цель доступа не разрешены");
            }

            if (repository.Find(request.PatientId) == null)
                return Error(404, "not_found", "Карта не найдена");
            lock (gate)
            {
                if (requests.TryGetValue(request.RequestId, out var old))
                    return old.Actor == actor.Id && old.Request == request ? Results.Ok(old.Reply) : Error(409, "request_id_conflict", "UUID уже использован с другими параметрами");
                var reply = new AccessReply(request.RequestId, request.PatientId, "allow", "summary");
                audit.Write(actor.Id, request.PatientId, "check_access", "allowed");
                requests.Add(request.RequestId, (actor.Id, request, reply));
                return Results.Ok(reply);
            }
        }).RequireAuthorization();
        return app;
    }
}
