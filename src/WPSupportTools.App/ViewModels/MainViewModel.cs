using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Procedures;
using WPSupportTools.Core;

namespace WPSupportTools.App.ViewModels;

public enum Screen
{
    Home,
    Running,
    Completed,
    Failed,
    Resolved,
    Escalated,
}

public sealed class MainViewModel : ObservableObject
{
    private readonly IAppLog _log;
    private readonly RunLogWriter _runLog;
    private readonly ISequenceExecutor _inProcessExecutor;
    private readonly ISequenceExecutor _elevatedExecutor;

    private MaintenanceSequence? _sequence;
    private Screen _screen = Screen.Home;
    private bool _isBusy;
    private bool _isSimulation;
    private string _runTitle = "";
    private string _failureTitle = "";
    private string _failureMessage = "";

    public MainViewModel(IAppLog log, ISequenceExecutor inProcessExecutor, ISequenceExecutor elevatedExecutor)
    {
        _log = log;
        _runLog = new RunLogWriter(log);
        _inProcessExecutor = inProcessExecutor;
        _elevatedExecutor = elevatedExecutor;

        Actions = ModuleCatalog.All.SelectMany(m => m.Actions).ToList();

        RunActionCommand = new RelayCommand(p => _ = RunAsync((SupportAction)p!), p => p is SupportAction && !IsBusy);
        ResolvedCommand = new RelayCommand(() => GiveFeedback(resolved: true));
        NotResolvedCommand = new RelayCommand(() => GiveFeedback(resolved: false));
        BackHomeCommand = new RelayCommand(() => CurrentScreen = Screen.Home, () => !IsBusy);
        OpenLogsCommand = new RelayCommand(OpenLogFolder);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty), () => !IsBusy);
    }

    public event EventHandler? RequestClose;

    public string VersionText => $"Versão {AppInfo.Version}";

    public IReadOnlyList<SupportAction> Actions { get; }

    public ObservableCollection<StepViewModel> Steps { get; } = [];

    public Screen CurrentScreen
    {
        get => _screen;
        private set => SetProperty(ref _screen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Verdadeiro se alguma etapa foi apenas simulada (comando real ainda não configurado).</summary>
    public bool IsSimulation
    {
        get => _isSimulation;
        private set => SetProperty(ref _isSimulation, value);
    }

    public string RunTitle
    {
        get => _runTitle;
        private set => SetProperty(ref _runTitle, value);
    }

    public string FailureTitle
    {
        get => _failureTitle;
        private set => SetProperty(ref _failureTitle, value);
    }

    public string FailureMessage
    {
        get => _failureMessage;
        private set => SetProperty(ref _failureMessage, value);
    }

    public ICommand RunActionCommand { get; }
    public ICommand ResolvedCommand { get; }
    public ICommand NotResolvedCommand { get; }
    public ICommand BackHomeCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CloseCommand { get; }

    private async Task RunAsync(SupportAction action)
    {
        if (IsBusy || !SequenceCatalog.TryCreate(action.SequenceId, out var sequence))
            return;

        _sequence = sequence;
        IsBusy = true;
        IsSimulation = false;
        RunTitle = sequence.Title;
        Steps.Clear();
        foreach (var step in sequence.Steps)
            Steps.Add(new StepViewModel(step.Id, step.DisplayName));
        CurrentScreen = Screen.Running;

        var alreadyElevated = Environment.IsPrivilegedProcess;
        var executor = sequence.RequiresElevation && !alreadyElevated ? _elevatedExecutor : _inProcessExecutor;
        var dispatcher = Application.Current.Dispatcher;
        var sink = new DelegateRunEventSink(e => dispatcher.Invoke(() => OnRunEvent(sequence, e)));

        _runLog.RunStarted(sequence, alreadyElevated);
        SequenceReport report;
        try
        {
            report = await executor.RunAsync(sequence, sink, CancellationToken.None);
        }
        catch (Exception ex)
        {
            report = SequenceReport.WithoutRunning(sequence.Id, SequenceOutcome.Failed,
                $"Erro inesperado: {ex.GetType().Name}: {ex.Message}");
        }

        _runLog.RunFinished(sequence, report);
        ShowResult(sequence, report);
        IsBusy = false;
    }

    private void OnRunEvent(MaintenanceSequence sequence, RunEvent runEvent)
    {
        _runLog.Handle(sequence, runEvent);

        switch (runEvent)
        {
            case StepStartedEvent started when IsKnownStep(started.Index, started.ProcedureId):
                Steps[started.Index].MarkRunning();
                break;

            case StepFinishedEvent finished when IsKnownStep(finished.Index, finished.ProcedureId):
                Steps[finished.Index].Apply(finished.Result);
                if (finished.Result.Outcome == ProcedureOutcome.NotConfigured)
                    IsSimulation = true;
                break;
        }
    }

    private bool IsKnownStep(int index, string procedureId) =>
        index >= 0 && index < Steps.Count && Steps[index].ProcedureId == procedureId;

    private void ShowResult(MaintenanceSequence sequence, SequenceReport report)
    {
        switch (report.Outcome)
        {
            case SequenceOutcome.Succeeded:
            case SequenceOutcome.Simulated:
                CurrentScreen = Screen.Completed;
                return;

            case SequenceOutcome.ElevationRequired:
                FailureTitle = "Permissão de administrador necessária";
                FailureMessage = report.Message ?? FailedStepText(sequence, report);
                break;

            default:
                FailureTitle = "Não foi possível concluir a correção";
                FailureMessage = report.FailedStepIndex is not null
                    ? FailedStepText(sequence, report)
                    : report.Message ?? "Ocorreu um erro durante a manutenção.";
                break;
        }

        // Qualquer etapa que tenha ficado "em andamento" não chegou a terminar.
        foreach (var step in Steps.Where(s => s.Status is StepStatus.Pending or StepStatus.Running))
            step.Apply(ProcedureResult.Skipped("Não executado."));

        CurrentScreen = Screen.Failed;
    }

    private static string FailedStepText(MaintenanceSequence sequence, SequenceReport report)
    {
        if (report.FailedStepIndex is not { } index || index >= sequence.Steps.Count || index >= report.Results.Count)
            return "Uma etapa da manutenção falhou.";

        return $"A etapa \"{sequence.Steps[index].DisplayName}\" falhou: {report.Results[index].Message}";
    }

    private void GiveFeedback(bool resolved)
    {
        if (_sequence is not null)
            _runLog.UserFeedback(_sequence.Id, resolved);

        CurrentScreen = resolved ? Screen.Resolved : Screen.Escalated;
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_log.LogDirectory);
            Process.Start(new ProcessStartInfo(_log.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir a pasta de logs:\n{_log.LogDirectory}\n\n{ex.Message}",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
