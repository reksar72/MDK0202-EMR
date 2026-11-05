using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Ais.Infrastructure;
using Ais.Presentation;
using Ais.UI;

namespace Ais.Desktop;
public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var connection = new Window
        {
            Title = "Подключение к учебному API ЭМК",
            Width = 690,
            Height = 330
        };
        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };
        panel.Children.Add(new TextBlock { Text = "Сначала запустите AIS.Gateway с параметром --demo.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Адрес API", Margin = new Thickness(0, 12, 0, 0) });
        var url = new TextBox
        {
            Text = "http://127.0.0.1:5274"
        };
        panel.Children.Add(url);
        panel.Children.Add(new TextBlock { Text = "Doctor token из консоли API", Margin = new Thickness(0, 12, 0, 0) });
        var token = new PasswordBox();
        panel.Children.Add(token);
        var button = new Button
        {
            Content = "Открыть клиент",
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(10)
        };
        panel.Children.Add(button);
        button.Click += (_, _) =>
        {
            try
            {
                if (!Uri.TryCreate(url.Text, UriKind.Absolute, out var address))
                    throw new ArgumentException("Некорректный адрес API");
                var http = new HttpClient
                {
                    BaseAddress = address,
                    Timeout = TimeSpan.FromSeconds(15)
                };
                var client = new MedicalApiClient(http, token.Password, address.IsLoopback);
                var window = new PatientWindow(new PatientViewModel(client));
                window.Closed += (_, _) => http.Dispose();
                app.MainWindow = window;
                window.Show();
                connection.Close();
            }
            catch (ArgumentException e)
            {
                MessageBox.Show(e.Message, "Ошибка подключения");
            }
        };
        connection.Content = panel;
        app.Run(connection);
    }
}
