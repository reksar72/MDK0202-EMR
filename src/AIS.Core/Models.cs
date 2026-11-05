namespace Ais.Core;
public sealed record SourcePatient(string ExternalId, string FullName, string BirthDate, string Sex, string PolicyNumber, string UpdatedAt);
public sealed record Patient(int ExternalId, Guid Id, string FullName, DateOnly BirthDate, string Sex, string PolicyNumber, DateTimeOffset UpdatedAt);
public sealed record PatientSummary(Guid Id, string FullName, string BirthDate, string Sex, string Note);
public sealed record AccessRequest(Guid RequestId, Guid PatientId, string Purpose, string WorkstationId);
public sealed record AccessReply(Guid RequestId, Guid PatientId, string Decision, string Scope);
public sealed record ApiError(string Code, string Message, Dictionary<string, string[]>? Errors = null);
public sealed record Actor(string Id, string Role, Guid[] AssignedPatients);
public interface IPatientRepository
{
    Patient? Find(Guid id);
    string Upsert(Patient patient);
}

public interface IAuditStore
{
    void Write(string actor, Guid patient, string action, string result);
}

public interface IMedicalApiClient
{
    Task<PatientSummary> GetSummaryAsync(Guid id, CancellationToken ct = default);
    Task<AccessReply> CheckAccessAsync(AccessRequest request, CancellationToken ct = default);
}

public static class SampleData
{
    public static readonly Guid PatientId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static Patient Patient => new(42, PatientId, "Пациент Учебный Один", new DateOnly(1990, 2, 14), "F", "0012345678901234", DateTimeOffset.Parse("2026-10-20T10:00:00+03:00"));
    public static SourcePatient Source => new("000042", "  Пациент   Учебный Один ", "14.02.1990", "Ж", "0012345678901234", "2026-10-20T10:00:00+03:00");
}
