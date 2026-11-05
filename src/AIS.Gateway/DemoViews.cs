using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Ais.Core;
using Ais.Infrastructure;
using Ais.Presentation;

namespace Ais.Gateway;
public static class DemoViews
{
    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    static string Pretty(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    static string Page(string title, string body) => $$"""
<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>{{title}}</title><style>body{background:#eef2f6;color:#172335;font:21px Arial;margin:0}main{max-width:1010px;margin:24px auto;background:white;padding:28px;border:1px solid #cbd4de;border-radius:8px}h1{font-size:29px}h2{font-size:24px}p{line-height:1.45}label{display:inline-block;margin:6px 12px 6px 0}input,select,button{font:19px Arial;padding:10px;border:1px solid #aabbcc;border-radius:4px}button{background:#245d8e;color:white;cursor:pointer}pre{font:20px Consolas,monospace;background:#f3f6f9;padding:14px;white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;font-size:20px}th,td{border:1px solid #ccd5de;padding:10px;text-align:left}th{background:#e7edf3}.ok{color:#19673a}.bad{color:#9e2929}footer{font-size:16px;color:#546170;border-top:1px solid #ddd;margin-top:20px;padding-top:12px}a{color:#245d8e}</style></head><body><main><p>МДК 02.02 · Вариант 2 · Электронная медицинская карта</p><h1>{{title}}</h1>{{body}}<footer>Шваб Роберт Эдуардович · И13_24_11 · C# / .NET 8<br>Все данные вымышленные. Локальный учебный сервер, без внешней медицинской системы.</footer></main></body></html>
""";
    public static void Map(WebApplication app, string doctorToken, string adminToken, string nurseToken)
    {
        app.MapGet("/", () => Results.Content(Page("Практические работы 3, 4 и 6", "<p><a href='/demo/mapping'>ПР 3 — сопоставление данных</a></p><p><a href='/demo/access'>ПР 4 — контракт доступа</a></p><p><a href='/demo/mvvm'>ПР 6 — проверка ViewModel</a></p>"), "text/html; charset=utf-8"));
        app.MapGet("/demo/mapping", () => Results.Content(Mapping(null), "text/html; charset=utf-8"));
        app.MapPost("/demo/mapping", async (HttpRequest r) => Results.Content(Mapping((await r.ReadFormAsync())["date"].ToString()), "text/html; charset=utf-8"));
        app.MapGet("/demo/access", () => Results.Content(AccessForm(""), "text/html; charset=utf-8"));
        app.MapPost("/demo/access", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            string role = form["role"].ToString();
            using var http = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:" + context.Connection.LocalPort),
                Timeout = TimeSpan.FromSeconds(10)
            };
            if (role != "Anonymous")
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role == "Admin" ? adminToken : role == "Nurse" ? nurseToken : doctorToken);
            string station = form["station"].ToString();
            var request = new AccessRequest(Guid.NewGuid(), SampleData.PatientId, form["purpose"].ToString(), station);
            using var response = await http.PostAsJsonAsync("api/v1/access/check", request);
            string body = await response.Content.ReadAsStringAsync();
            string result = $"<div id='result'><h2>Результат запроса</h2><p>Роль: {H(role)} · рабочая станция: {H(station)} · HTTP {(int)response.StatusCode}</p><pre>" + H(Pretty(request)) + "</pre><pre>" + H(body) + "</pre></div>";
            return Results.Content(AccessForm(result, role, station, form["purpose"].ToString()), "text/html; charset=utf-8");
        });
        app.MapGet("/demo/mvvm", () => Results.Content(VmForm(""), "text/html; charset=utf-8"));
        app.MapPost("/demo/mvvm", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            using var http = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:" + context.Connection.LocalPort),
                Timeout = TimeSpan.FromSeconds(10)
            };
            var vm = new PatientViewModel(new MedicalApiClient(http, doctorToken, true));
            vm.PatientId = form["patient"].ToString();
            int notifications = 0;
            vm.PropertyChanged += (_, _) => notifications++;
            await vm.LoadAsync();
            string result = $"<div id='result'><h2>Состояние ViewModel после команды</h2><table><tr><th>Свойство</th><th>Значение</th></tr><tr><td>PatientName</td><td>{H(vm.PatientName)}</td></tr><tr><td>BirthDate</td><td>{H(vm.BirthDate)}</td></tr><tr><td>Status</td><td>{H(vm.Status)}</td></tr><tr><td>IsBusy</td><td>{vm.IsBusy}</td></tr><tr><td>PropertyChanged</td><td>{notifications} уведомлений</td></tr></table><p>Выполнены настоящий HTTP-запрос и код ViewModel. Это проверочный веб-интерфейс, не окно WPF.</p></div>";
            return Results.Content(VmForm(result, vm.PatientId), "text/html; charset=utf-8");
        });
    }

    static string AccessForm(string result, string role = "Doctor", string station = "PC-001", string purpose = "Treatment") => Page("ПР 4 — JSON-контракт и контроль доступа", "<form method='post'><label>Роль <select name='role'>" + string.Concat(new[] { "Doctor", "Nurse", "Admin", "Anonymous" }.Select(x => "<option" + (x == role ? " selected" : "") + ">" + x + "</option>")) + "</select></label><label>Цель <select name='purpose'>" + string.Concat(new[] { "Treatment", "NursingCare" }.Select(x => "<option" + (x == purpose ? " selected" : "") + ">" + x + "</option>")) + "</select></label><label>Станция <input name='station' value='" + H(station) + "'></label><button>Отправить запрос</button></form>" + result);
    static string VmForm(string result, string? patientId = null) => Page("ПР 6 — проверка MVVM на C#", "<form method='post'><label>UUID пациента <input name='patient' size='38' value='" + H(patientId ?? SampleData.PatientId.ToString()) + "'></label><button>Выполнить LoadCommand</button></form>" + result);
    static string Mapping(string? date)
    {
        string result = "";
        if (date != null)
        {
            try
            {
                var patient = PatientMapper.Map(SampleData.Source with { BirthDate = date }, SampleData.PatientId, new DateOnly(2026, 11, 5));
                result = "<div id='result'><h2>Результат сопоставления</h2><p class='ok'>Проверки полей пройдены</p><pre>" + H(Pretty(patient)) + "</pre><p>Ведущие нули полиса сохранены; время приведено к UTC.</p></div>";
            }
            catch (FormatException e)
            {
                result = "<div id='result'><h2>Результат сопоставления</h2><p class='bad'>Отклонено: " + H(e.Message) + "</p></div>";
            }
        }

        return Page("ПР 3 — Data Mapping на C#", "<form method='post'><label>Дата из реестра <input name='date' value='" + H(date ?? "14.02.1990") + "'></label><button>Преобразовать</button></form>" + result);
    }
}
