using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Ais.Core;

namespace Ais.Presentation;
public sealed class PatientViewModel : INotifyPropertyChanged
{
    readonly IMedicalApiClient api;
    string patientId = SampleData.PatientId.ToString(), status = "Готово к загрузке", name = "", birth = "";
    bool busy;
    public PatientViewModel(IMedicalApiClient client)
    {
        api = client;
        LoadCommand = new AsyncCommand(LoadAsync, () => !IsBusy);
    }

    public string PatientId
    {
        get => patientId;
        set
        {
            patientId = value;
            Changed();
        }
    }

    public string Status
    {
        get => status;
        private set
        {
            status = value;
            Changed();
        }
    }

    public string PatientName
    {
        get => name;
        private set
        {
            name = value;
            Changed();
        }
    }

    public string BirthDate
    {
        get => birth;
        private set
        {
            birth = value;
            Changed();
        }
    }

    public bool IsBusy
    {
        get => busy;
        private set
        {
            busy = value;
            Changed();
            LoadCommand.Refresh();
        }
    }

    public AsyncCommand LoadCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;
        PatientName = "";
        BirthDate = "";
        if (!Guid.TryParse(PatientId, out var id) || id == Guid.Empty)
        {
            Status = "Введите непустой UUID пациента";
            return;
        }

        IsBusy = true;
        Status = "Загрузка";
        try
        {
            var result = await api.GetSummaryAsync(id);
            PatientName = result.FullName;
            BirthDate = result.BirthDate;
            Status = "Карта загружена";
        }
        catch (HttpRequestException e)
        {
            Status = e.Message;
        }
        catch (InvalidDataException e)
        {
            Status = e.Message;
        }
        catch (OperationCanceledException)
        {
            Status = "Загрузка отменена";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class AsyncCommand(Func<Task> action, Func<bool> canExecute) : ICommand
{
    bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && canExecute();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;
        running = true;
        Refresh();
        try
        {
            await action();
        }
        finally
        {
            running = false;
            Refresh();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
