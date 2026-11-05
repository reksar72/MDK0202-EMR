using System.Text.Json;
using System.Text.Json.Serialization;
using Ais.Core;
using Ais.Infrastructure;

string input = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "data/source-patient.json");
string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Environment.CurrentDirectory, "verification");
Directory.CreateDirectory(output);
Guid targetId = args.Length > 2 ? Guid.Parse(args[2]) : SampleData.PatientId;
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
};
var source = JsonSerializer.Deserialize<SourcePatient>(File.ReadAllText(input), options) ?? throw new FormatException("Пустой JSON источника");
var patient = PatientMapper.Map(source, targetId, new DateOnly(2026, 11, 5));
var repository = new SqlitePatientRepository(Path.Combine(output, "mapping-demo.db"));
Console.WriteLine(repository.Upsert(patient));
var json = JsonSerializer.Serialize(patient, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
File.WriteAllText(Path.Combine(output, "mapped-patient.json"), json);
Console.WriteLine(json);
