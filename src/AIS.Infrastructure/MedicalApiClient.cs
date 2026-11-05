using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ais.Core;

namespace Ais.Infrastructure;
public sealed class MedicalApiClient : IMedicalApiClient
{
    readonly HttpClient http;
    public MedicalApiClient(HttpClient client, string token, bool allowLoopbackDemo = false)
    {
        http = client;
        if (http.BaseAddress == null || (http.BaseAddress.Scheme != "https" && !(allowLoopbackDemo && http.BaseAddress.IsLoopback)))
            throw new ArgumentException("Нужен HTTPS; HTTP разрешён только локальному учебному серверу");
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Не указан токен");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<PatientSummary> GetSummaryAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/v1/patients/{id}/summary", ct);
        await Ensure(response, ct);
        return await Read<PatientSummary>(response, ct);
    }

    public async Task<AccessReply> CheckAccessAsync(AccessRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/v1/access/check", request, ct);
        await Ensure(response, ct);
        return await Read<AccessReply>(response, ct);
    }

    static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidDataException("Пустой ответ API");
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new InvalidDataException("Некорректный JSON ответа", e);
        }
    }

    static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Требуется действующий токен",
            HttpStatusCode.Forbidden => "Нет доступа к этой карте",
            HttpStatusCode.BadRequest => "Ошибка параметров запроса",
            _ => "Шлюз не выполнил запрос"
        };
        await response.Content.LoadIntoBufferAsync();
        ct.ThrowIfCancellationRequested();
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
