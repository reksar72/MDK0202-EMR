using System.Globalization;
using System.Text.RegularExpressions;

namespace Ais.Core;
public static class PatientMapper
{
    public static Patient Map(SourcePatient source, Guid targetId, DateOnly today)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("Пустой ID пациента");
        if (!Regex.IsMatch(source.ExternalId ?? "", @"\A[0-9]{1,9}\z") || !int.TryParse(source.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            throw new FormatException("externalId: положительное целое до 9 цифр");
        if ((source.FullName ?? "").Any(char.IsControl))
            throw new FormatException("fullName: управляющие символы запрещены");
        var name = Regex.Replace((source.FullName ?? "").Trim(), @"\s+", " ");
        if (name.Length < 3 || name.Length > 150 || name.Any(char.IsControl))
            throw new FormatException("fullName: длина 3–150, без управляющих символов");
        if (!DateOnly.TryParseExact(source.BirthDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth) || birth < new DateOnly(1900, 1, 1) || birth > today)
            throw new FormatException("birthDate: неверная календарная дата");
        string sex = (source.Sex ?? "").Trim().ToUpperInvariant() switch
        {
            "М" => "M",
            "Ж" => "F",
            "Н" => "U",
            _ => throw new FormatException("sex: допустимы М, Ж, Н")};
        if (!Regex.IsMatch(source.PolicyNumber ?? "", @"\A[0-9]{16}\z"))
            throw new FormatException("policyNumber: ровно 16 цифр, строковый тип");
        if (!Regex.IsMatch(source.UpdatedAt ?? "", @"(?:Z|[+-][0-9]{2}:[0-9]{2})\z") || !DateTimeOffset.TryParse(source.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated))
            throw new FormatException("updatedAt: ISO 8601 с часовым смещением");
        return new(id, targetId, name, birth, sex, source.PolicyNumber!, updated.ToUniversalTime());
    }
}
