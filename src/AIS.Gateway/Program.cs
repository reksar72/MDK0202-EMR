using System.Security.Cryptography;
using Ais.Core;
using Ais.Infrastructure;
using Ais.Gateway;

if (!args.Contains("--demo"))
    throw new InvalidOperationException("Запуск учебного сервера: --demo. Для рабочего контура нужны отдельные OIDC/TLS и управление ключами.");
var folder = Path.Combine(Path.GetTempPath(), "EmrDemo_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var repository = new SqlitePatientRepository(Path.Combine(folder, "patients.db"));
repository.Upsert(SampleData.Patient);
var audit = new EncryptedAuditStore(Path.Combine(folder, "audit.enc"), RandomNumberGenerator.GetBytes(32));
var tokens = new TokenDirectory();
string doctor = tokens.Issue(new Actor("doctor-demo", "Doctor", new[] { SampleData.PatientId }));
string admin = tokens.Issue(new Actor("admin-demo", "Admin", Array.Empty<Guid>()));
string nurse = tokens.Issue(new Actor("nurse-demo", "Nurse", new[] { SampleData.PatientId }));
var app = GatewayFactory.Create(repository, audit, tokens, new[] { "--urls", "http://127.0.0.1:5274" });
DemoViews.Map(app, doctor, admin, nurse);
Console.WriteLine("Локальный API: http://127.0.0.1:5274");
Console.WriteLine("Doctor token (только для этого учебного запуска): " + doctor);
Console.WriteLine("Nurse token (учебный): " + nurse);
try
{
    await app.RunAsync();
}
finally
{
    await app.DisposeAsync();
    try
    {
        Directory.Delete(folder, true);
    }
    catch (IOException)
    {
    }
}
