using System.Text.RegularExpressions;

namespace Ais.Core;
public static class AccessPolicy
{
    public static Dictionary<string, string[]> Validate(AccessRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.RequestId == Guid.Empty)
            errors["requestId"] = new[]
            {
                "Нужен непустой UUID"
            };
        if (r.PatientId == Guid.Empty)
            errors["patientId"] = new[]
            {
                "Нужен непустой UUID"
            };
        if (r.Purpose is not ("Treatment" or "NursingCare"))
            errors["purpose"] = new[]
            {
                "Treatment или NursingCare"
            };
        if (!Regex.IsMatch(r.WorkstationId ?? "", @"\A[A-Z]{2}-[0-9]{3}\z"))
            errors["workstationId"] = new[]
            {
                "Формат AA-000"
            };
        return errors;
    }

    public static bool CanRead(Actor actor, Guid patient) => (actor.Role is "Doctor" or "Nurse") && actor.AssignedPatients.Contains(patient);
    public static bool CanCheck(Actor actor, AccessRequest r) => CanRead(actor, r.PatientId) && ((actor.Role == "Doctor" && r.Purpose == "Treatment") || (actor.Role == "Nurse" && r.Purpose == "NursingCare"));
}
