using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ais.Presentation;

namespace Ais.UI;
public sealed class PatientWindow : Window
{
    public PatientWindow(PatientViewModel viewModel)
    {
        Title = "ПР 6 — Электронная медицинская карта";
        Width = 760;
        Height = 430;
        DataContext = viewModel;
        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };
        panel.Children.Add(new TextBlock { Text = "Учебная электронная медицинская карта", FontSize = 22, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "UUID назначенного пациента" });
        var id = new TextBox
        {
            FontSize = 16,
            Margin = new Thickness(0, 6, 0, 12)
        };
        id.SetBinding(TextBox.TextProperty, new Binding("PatientId") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(id);
        var load = new Button
        {
            Content = "Загрузить карту",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        load.SetBinding(Button.CommandProperty, new Binding("LoadCommand"));
        panel.Children.Add(load);
        foreach (var property in new[]
        {
            "PatientName",
            "BirthDate",
            "Status"
        }

        )
        {
            var text = new TextBlock
            {
                FontSize = 16,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            text.SetBinding(TextBlock.TextProperty, new Binding(property));
            panel.Children.Add(text);
        }

        panel.Children.Add(new TextBlock { Text = "Данные вымышленные. Медицинские решения программа не принимает.", Margin = new Thickness(0, 20, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = panel;
    }
}
