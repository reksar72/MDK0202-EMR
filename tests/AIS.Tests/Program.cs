using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Ais.Core;
using Ais.Infrastructure;
using Ais.Gateway;
using Ais.Presentation;

int passed = 0, failed = 0;
var checks = new List<object>();
string evidence = Path.GetFullPath(args.Length > 0 ? args[0] : "verification");
Directory.CreateDirectory(evidence);
string temp = Path.Combine(Path.GetTempPath(), "EmrTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
void Check(bool value, string name)
{
    if (!value)
        throw new Exception(name);
    passed++;
    checks.Add(new { name, status = "PASS" });
    Console.WriteLine("PASS " + name);
}

void Reject<T>(Action action, string name)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        Check(true, name);
        return;
    }

    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}

var today = new DateOnly(2026, 11, 5);
var source = SampleData.Source;
var id = SampleData.PatientId;
try
{
    var projectRoot = Directory.GetParent(evidence)!.FullName;
    var projectFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
    var dependencies = projectFiles.ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => System.Xml.Linq.XDocument.Load(p).Descendants("ProjectReference").Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/'))).ToArray());
    Check(dependencies.Count == 8 && dependencies["AIS.Core"].Length == 0, "Architecture has eight projects and independent Core");
    Check(!dependencies["AIS.UI"].Contains("AIS.Infrastructure"), "View does not reference Infrastructure");
    Check(dependencies.Values.SelectMany(x => x).All(dependencies.ContainsKey), "All project references resolve");
    bool Visit(string node, HashSet<string> active, HashSet<string> visited)
    {
        if (active.Contains(node))
            return false;
        if (visited.Contains(node))
            return true;
        active.Add(node);
        foreach (var next in dependencies[node])
            if (!Visit(next, active, visited))
                return false;
        active.Remove(node);
        visited.Add(node);
        return true;
    }

    Check(dependencies.Keys.All(n => Visit(n, new HashSet<string>(), new HashSet<string>())), "Project dependency graph is acyclic");
    File.WriteAllText(Path.Combine(evidence, "architecture.json"), JsonSerializer.Serialize(dependencies, new JsonSerializerOptions { WriteIndented = true }));
    var patient = PatientMapper.Map(source, id, today);
    Check(patient.ExternalId == 42 && patient.FullName == "Пациент Учебный Один", "Mapping normalizes external ID and spaces");
    Check(patient.PolicyNumber == "0012345678901234", "Mapping preserves policy leading zeros");
    Check(patient.BirthDate == new DateOnly(1990, 2, 14) && patient.Sex == "F", "Mapping date and sex");
    Check(patient.UpdatedAt.Offset == TimeSpan.Zero && patient.UpdatedAt.Hour == 7, "Mapping converts source offset to UTC");
    Reject<FormatException>(() => PatientMapper.Map(source with { BirthDate = "31.02.2020" }, id, today), "Mapping rejects impossible date");
    Reject<FormatException>(() => PatientMapper.Map(source with { BirthDate = "01.01.2030" }, id, today), "Mapping rejects future birth date");
    Reject<FormatException>(() => PatientMapper.Map(source with { PolicyNumber = "123" }, id, today), "Mapping rejects short policy");
    Reject<FormatException>(() => PatientMapper.Map(source with { ExternalId = "-1" }, id, today), "Mapping rejects negative source ID");
    Reject<FormatException>(() => PatientMapper.Map(source with { Sex = "other" }, id, today), "Mapping rejects unknown sex code");
    Reject<FormatException>(() => PatientMapper.Map(source with { UpdatedAt = "2026-10-20T10:00:00" }, id, today), "Mapping rejects timestamp without offset");
    Reject<FormatException>(() => PatientMapper.Map(source with { FullName = "Bad\nName" }, id, today), "Mapping rejects control characters");
    var repository = new SqlitePatientRepository(Path.Combine(temp, "patients.db"));
    Check(repository.Upsert(patient) == "CREATED", "SQLite creates a mapped patient");
    Check(repository.Upsert(patient) == "DUPLICATE", "Same mapping is idempotent");
    Check(repository.Upsert(patient with { UpdatedAt = patient.UpdatedAt.AddMinutes(-1) }) == "STALE", "Older update does not overwrite record");
    Reject<InvalidOperationException>(() => repository.Upsert(patient with { FullName = "Другая запись" }), "Same version with changed data is a conflict");
    Reject<InvalidOperationException>(() => repository.Upsert(patient with { Id = Guid.NewGuid() }), "External ID cannot switch patient UUID");
    Check(repository.Upsert(patient with { UpdatedAt = patient.UpdatedAt.AddMinutes(1) }) == "UPDATED", "Newer mapping updates record");
    Check(repository.Find(id)!.PolicyNumber == source.PolicyNumber, "SQLite round-trip preserves string policy");
    var key = RandomNumberGenerator.GetBytes(32);
    var auditPath = Path.Combine(temp, "audit.enc");
    var audit = new EncryptedAuditStore(auditPath, key);
    audit.Write("doctor-test", id, "read", "allowed");
    audit.Write("doctor-test", id, "read", "allowed");
    var lines = File.ReadAllLines(auditPath);
    Check(!File.ReadAllText(auditPath).Contains("doctor-test") && !File.ReadAllText(auditPath).Contains(id.ToString()), "Audit file contains no clear actor or patient ID");
    Check(audit.DecryptForVerification(lines[0]).Contains("doctor-test"), "AES-GCM audit decrypts with the correct key");
    Check(JsonDocument.Parse(lines[0]).RootElement.GetProperty("Nonce").GetString() != JsonDocument.Parse(lines[1]).RootElement.GetProperty("Nonce").GetString(), "Audit uses unique random nonce");
    var envelope = JsonSerializer.Deserialize<AuditEnvelope>(lines[0])!;
    var tag = Convert.FromBase64String(envelope.Tag);
    tag[0] ^= 1;
    Reject<CryptographicException>(() => audit.DecryptForVerification(JsonSerializer.Serialize(envelope with { Tag = Convert.ToBase64String(tag) })), "Audit rejects modified authentication tag");
    Reject<ArgumentException>(() => new EncryptedAuditStore(auditPath, new byte[16]), "Audit requires a 256-bit key");
    var tokens = new TokenDirectory();
    var doctor = tokens.Issue(new Actor("doctor-test", "Doctor", new[] { id }));
    var admin = tokens.Issue(new Actor("admin-test", "Admin", Array.Empty<Guid>()));
    var unassigned = tokens.Issue(new Actor("other-test", "Doctor", Array.Empty<Guid>()));
    var missingId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    var missingToken = tokens.Issue(new Actor("assigned-missing", "Doctor", new[] { missingId }));
    var nurse = tokens.Issue(new Actor("nurse-test", "Nurse", new[] { id }));
    var app = GatewayFactory.Create(repository, audit, tokens);
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(address)
        };
        Check((await http.GetAsync("health")).IsSuccessStatusCode, "API readiness");
        Check((await http.GetAsync($"api/v1/patients/{id}/summary")).StatusCode == HttpStatusCode.Unauthorized, "API rejects anonymous summary with 401");
        http.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        Check((await http.GetAsync($"api/v1/patients/{id}/summary")).StatusCode == HttpStatusCode.Forbidden, "Admin cannot read clinical summary");
        http.DefaultRequestHeaders.Authorization = new("Bearer", unassigned);
        Check((await http.GetAsync($"api/v1/patients/{id}/summary")).StatusCode == HttpStatusCode.Forbidden, "Doctor without assignment cannot read patient");
        http.DefaultRequestHeaders.Authorization = new("Bearer", doctor);
        using var summary = await http.GetAsync($"api/v1/patients/{id}/summary");
        var summaryJson = await summary.Content.ReadAsStringAsync();
        Check(summary.IsSuccessStatusCode && JsonSerializer.Deserialize<PatientSummary>(summaryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.FullName == patient.FullName, "API summary deserializes into C# model");
        Check(!summaryJson.Contains("policyNumber", StringComparison.OrdinalIgnoreCase), "Summary omits policy number");
        var request = new AccessRequest(Guid.NewGuid(), id, "Treatment", "PC-001");
        using var first = await http.PostAsJsonAsync("api/v1/access/check", request);
        string firstJson = await first.Content.ReadAsStringAsync();
        Check(first.IsSuccessStatusCode && firstJson.Contains("allow"), "Access contract accepts assigned Doctor");
        int countBefore = File.ReadAllLines(auditPath).Length;
        Check((await http.PostAsJsonAsync("api/v1/access/check", request)).IsSuccessStatusCode && File.ReadAllLines(auditPath).Length == countBefore, "Repeated request ID does not duplicate access audit");
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { WorkstationId = "PC-002" })).StatusCode == HttpStatusCode.Conflict, "Request ID collision returns 409");
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { RequestId = Guid.NewGuid(), WorkstationId = "bad" })).StatusCode == HttpStatusCode.BadRequest, "Invalid workstation returns 400");
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { RequestId = Guid.Empty })).StatusCode == HttpStatusCode.BadRequest, "Empty request UUID returns 400");
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { RequestId = Guid.NewGuid(), Purpose = "NursingCare" })).StatusCode == HttpStatusCode.Forbidden, "Doctor cannot spoof nursing purpose");
        using (var spoof = await http.PostAsync("api/v1/access/check", new StringContent(JsonSerializer.Serialize(new { requestId = Guid.NewGuid(), patientId = id, purpose = "Treatment", workstationId = "PC-001", role = "Admin" }), Encoding.UTF8, "application/json")))
            Check(spoof.StatusCode == HttpStatusCode.BadRequest, "Unknown client role field is rejected");
        using (var malformed = await http.PostAsync("api/v1/access/check", new StringContent("{\"requestId\":\"bad\"}", Encoding.UTF8, "application/json")))
            Check(malformed.StatusCode == HttpStatusCode.BadRequest, "Malformed UUID is rejected");
        http.DefaultRequestHeaders.Authorization = new("Bearer", missingToken);
        Check((await http.GetAsync($"api/v1/patients/{missingId}/summary")).StatusCode == HttpStatusCode.NotFound, "Assigned missing record returns 404");
        var client = new MedicalApiClient(new HttpClient { BaseAddress = new Uri(address) }, doctor, true);
        var vm = new PatientViewModel(client);
        int notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        await vm.LoadAsync();
        Check(vm.Status == "Карта загружена" && vm.PatientName == patient.FullName && !vm.IsBusy, "MVVM load command completes against real API");
        Check(notifications >= 6 && vm.LoadCommand.CanExecute(null), "MVVM sends notifications and re-enables command");
        vm.PatientId = "not-a-guid";
        await vm.LoadAsync();
        Check(vm.PatientName == "" && vm.Status.Contains("UUID"), "MVVM rejects invalid UUID and clears old data");
        var deniedClient = new MedicalApiClient(new HttpClient { BaseAddress = new Uri(address) }, admin, true);
        var deniedVm = new PatientViewModel(deniedClient);
        await deniedVm.LoadAsync();
        Check(deniedVm.PatientName == "" && deniedVm.Status == "Нет доступа к этой карте" && !deniedVm.IsBusy, "MVVM handles HTTP 403 without exposing old data");
        Reject<ArgumentException>(() => new MedicalApiClient(new HttpClient { BaseAddress = new Uri("http://example.com") }, doctor), "Client rejects nonlocal HTTP");
        http.DefaultRequestHeaders.Authorization = new("Bearer", nurse);
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { RequestId = Guid.NewGuid(), Purpose = "NursingCare" })).IsSuccessStatusCode, "Assigned Nurse may request NursingCare");
        Check((await http.PostAsJsonAsync("api/v1/access/check", request with { RequestId = Guid.NewGuid() })).StatusCode == HttpStatusCode.Forbidden, "Nurse cannot claim Treatment purpose");
        http.DefaultRequestHeaders.Authorization = new("Bearer", doctor);
        Check((await http.PostAsync("api/v1/access/check", new StringContent("null", Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "Null request JSON returns 400");
        Check((await http.PostAsync("api/v1/access/check", new StringContent("text"))).StatusCode == HttpStatusCode.UnsupportedMediaType, "Non-JSON request returns 415");
        var badJson = new MedicalApiClient(new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") })) { BaseAddress = new Uri(address) }, doctor, true);
        var badVm = new PatientViewModel(badJson);
        await badVm.LoadAsync();
        Check(badVm.Status == "Некорректный JSON ответа" && !badVm.IsBusy, "MVVM displays malformed response error");
        File.WriteAllText(Path.Combine(evidence, "api-responses.json"), JsonSerializer.Serialize(new { summary = JsonSerializer.Deserialize<JsonElement>(summaryJson), access = JsonSerializer.Deserialize<JsonElement>(firstJson) }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
    finally
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
catch (Exception e)
{
    failed++;
    checks.Add(new { name = e.GetType().Name + ": " + e.Message, status = "FAIL" });
    Console.WriteLine("FAIL " + e);
}
finally
{
    File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { executedAt = DateTimeOffset.UtcNow, passed, failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"TOTAL PASS={passed}; FAIL={failed}");
    try
    {
        Directory.Delete(temp, true);
    }
    catch (IOException)
    {
    }
}

return failed == 0 ? 0 : 1;
sealed class StubHandler(Func<HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply());
}
