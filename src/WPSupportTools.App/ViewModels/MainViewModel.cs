using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Procedures;
using WPSupportTools.Core;
using WPSupportTools.Core.Telemetry;
using WPSupportTools.App.Service;
using WPSupportTools.App.Elevation;

namespace WPSupportTools.App.ViewModels;

public enum Screen
{
    Setup,
    Home,
    Choose,
    Input,
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
    private readonly ISequenceExecutor _executor;
    private readonly UsageReporter _usage;
    private readonly UnitStore _unitStore;
    private Guid _runId;

    private MaintenanceSequence? _sequence;
    private Screen _screen = Screen.Home;
    private bool _isBusy;
    private bool _isSimulation;
    private string _runTitle = "";
    private string _failureTitle = "";
    private string _failureMessage = "";
    private SupportAction? _choiceAction;
    private string _completionHint = "";
    private SupportAction? _inputAction;
    private string _inputValue = "";
    private string _inputError = "";

    public MainViewModel(IAppLog log, ISequenceExecutor executor, UsageReporter usage, UnitStore unitStore)
    {
        _log = log;
        _runLog = new RunLogWriter(log);
        _executor = executor;
        _usage = usage;
        _unitStore = unitStore;

        Actions = ModuleCatalog.All.SelectMany(m => m.Actions).ToList();

        RunActionCommand = new RelayCommand(p => StartAction((SupportAction)p!), p => p is SupportAction && !IsBusy);
        ResolvedCommand = new RelayCommand(() => GiveFeedback(resolved: true));
        NotResolvedCommand = new RelayCommand(() => GiveFeedback(resolved: false));
        BackHomeCommand = new RelayCommand(() => CurrentScreen = Screen.Home, () => !IsBusy);
        OpenLogsCommand = new RelayCommand(OpenLogFolder);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => !IsBusy);
        SaveUnitCommand = new RelayCommand(SaveUnit);
        ChangeUnitCommand = new RelayCommand(StartChangingUnit, () => !IsBusy);
        CancelUnitCommand = new RelayCommand(() => CurrentScreen = Screen.Home, () => Unit is not null);
        ConfirmInputCommand = new RelayCommand(ConfirmInput, () => !IsBusy);
        RefreshInstallState();

        Unit = _unitStore.Load();
        _usage.Unit = Unit;
        if (Unit is null)
            StartChangingUnit();
        _ = _usage.FlushAsync();   // envia o que tiver ficado guardado de usos anteriores
    }

    // ---------------- Unidade (primeiro acesso) ----------------

    private UnitInfo? _unit;
    private string? _selectedLaboratory;
    private string _unitName = "";
    private string _unitError = "";

    public IReadOnlyList<string> Laboratories => UnitInfo.Laboratories;

    /// <summary>Unidade deste computador (null até ser preenchida).</summary>
    public UnitInfo? Unit
    {
        get => _unit;
        private set
        {
            if (SetProperty(ref _unit, value))
            {
                OnPropertyChanged(nameof(HasUnit));
            }
        }
    }

    public bool HasUnit => Unit is not null;

    public string? SelectedLaboratory
    {
        get => _selectedLaboratory;
        set => SetProperty(ref _selectedLaboratory, value);
    }

    public string UnitName
    {
        get => _unitName;
        set => SetProperty(ref _unitName, value);
    }

    public string UnitError
    {
        get => _unitError;
        private set => SetProperty(ref _unitError, value);
    }

    public ICommand SaveUnitCommand { get; }
    public ICommand ChangeUnitCommand { get; }
    public ICommand CancelUnitCommand { get; }

    private void StartChangingUnit()
    {
        SelectedLaboratory = Unit?.Laboratory;
        UnitName = Unit?.Unit ?? "";
        UnitError = "";
        CurrentScreen = Screen.Setup;
    }

    private void SaveUnit()
    {
        var error = UnitInfo.TryCreate(SelectedLaboratory, UnitName, out var info);
        if (error is not null || info is null)
        {
            UnitError = error ?? "Confira os dados.";
            return;
        }

        try
        {
            var path = _unitStore.Save(info);
            _log.Write(LogLevel.Info, $"Unidade definida: {info.DisplayName} (em {path}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UnitError = $"Não foi possível salvar: {ex.Message}";
            return;
        }

        Unit = info;
        _usage.Unit = info;
        CurrentScreen = Screen.Home;
    }

    private bool _canInstall;
    private string _installStatus = "";

    /// <summary>Mostra "Instalar neste computador" quando esta cópia não é a instalada com o serviço.</summary>
    public bool CanInstall
    {
        get => _canInstall;
        private set => SetProperty(ref _canInstall, value);
    }

    public string InstallStatus
    {
        get => _installStatus;
        private set => SetProperty(ref _installStatus, value);
    }

    public ICommand InstallCommand { get; }

    private void RefreshInstallState()
    {
        var installed = Installer.IsServiceInstalled();
        var fromInstallDir = Installer.IsRunningFromInstallDirectory();
        CanInstall = !(installed && fromInstallDir);
        InstallStatus = installed && fromInstallDir
            ? (Installer.IsServiceRunning() ? " · instalado, atualização automática" : " · instalado (serviço parado)")
            : "";
    }

    private async Task InstallAsync()
    {
        var confirm = MessageBox.Show(
            "Instalar o WP Support Tools neste computador?\n\n" +
            "• Copia o programa para Arquivos de Programas e cria um atalho na área de trabalho.\n" +
            "• Instala o serviço que corrige a impressão sem pedir senha e mantém o programa atualizado.\n\n" +
            "O Windows vai pedir a senha de administrador uma única vez.",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        int exitCode;
        try
        {
            exitCode = await Task.Run(() =>
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = "--install",
                    UseShellExecute = true,
                    Verb = "runas",
                });
                if (process is null)
                    return -1;
                process.WaitForExit();
                return process.ExitCode;
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == NativeMethods.ErrorCancelled)
        {
            _log.Write(LogLevel.Info, "Instalação cancelada: permissão de administrador não concedida.");
            return;
        }

        RefreshInstallState();
        if (exitCode != 0)
            return;   // o instalador já mostrou o erro

        _log.Write(LogLevel.Info, "WP Support Tools instalado neste computador.");
        if (!Installer.IsRunningFromInstallDirectory() &&
            MessageBox.Show("Instalado com sucesso. Um atalho foi criado na área de trabalho.\n\nAbrir agora a versão instalada?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            ((App)Application.Current).OpenInstalledAndExit();
        }
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

    /// <summary>Ação cuja pergunta está na tela (ex.: "Problema na etiquetadora").</summary>
    public SupportAction? ChoiceAction
    {
        get => _choiceAction;
        private set => SetProperty(ref _choiceAction, value);
    }

    /// <summary>Ação que está pedindo uma informação antes de executar (ex.: licença do UniLab).</summary>
    public SupportAction? InputAction
    {
        get => _inputAction;
        private set => SetProperty(ref _inputAction, value);
    }

    public string InputValue
    {
        get => _inputValue;
        set => SetProperty(ref _inputValue, value);
    }

    public string InputError
    {
        get => _inputError;
        private set => SetProperty(ref _inputError, value);
    }

    public ICommand ConfirmInputCommand { get; }

    /// <summary>Orientação final da ação executada (ex.: "Tente imprimir novamente.").</summary>
    public string CompletionHint
    {
        get => _completionHint;
        private set => SetProperty(ref _completionHint, value);
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

    private void StartAction(SupportAction action)
    {
        if (action.Choices is { Count: > 0 })
        {
            ChoiceAction = action;
            CurrentScreen = Screen.Choose;
            return;
        }

        if (action.Input is not null)
        {
            InputAction = action;
            InputValue = "";
            InputError = "";
            CurrentScreen = Screen.Input;
            return;
        }

        _ = RunAsync(action);
    }

    private void ConfirmInput()
    {
        if (InputAction?.Input is not { } prompt)
            return;

        var (value, error) = prompt.Normalize(InputValue);
        if (value is null)
        {
            InputError = error ?? "Confira o que foi digitado.";
            return;
        }

        InputError = "";
        _ = RunAsync(InputAction, (prompt.StateKey, value));
    }

    private async Task RunAsync(SupportAction action, (string Key, string Value)? input = null)
    {
        if (IsBusy || action.SequenceId is null || !SequenceCatalog.TryCreate(action.SequenceId, out var sequence))
            return;
        if (input is { } answer)
            sequence.Inputs[answer.Key] = answer.Value;

        CompletionHint = action.CompletionHint;
        _sequence = sequence;
        _runId = Guid.NewGuid();
        IsBusy = true;
        IsSimulation = false;
        RunTitle = sequence.Title;
        Steps.Clear();
        foreach (var step in sequence.Steps)
            Steps.Add(new StepViewModel(step.Id, step.DisplayName));
        CurrentScreen = Screen.Running;

        var dispatcher = Application.Current.Dispatcher;
        var sink = new DelegateRunEventSink(e => dispatcher.Invoke(() => OnRunEvent(sequence, e)));

        _runLog.RunStarted(sequence, Environment.IsPrivilegedProcess);
        SequenceReport report;
        try
        {
            report = await _executor.RunAsync(sequence, sink, CancellationToken.None);
        }
        catch (Exception ex)
        {
            report = SequenceReport.WithoutRunning(sequence.Id, SequenceOutcome.Failed,
                $"Erro inesperado: {ex.GetType().Name}: {ex.Message}");
        }

        _runLog.RunFinished(sequence, report);
        _usage.RunFinished(_runId, report, report.FailedStepIndex is { } failed && failed < sequence.Steps.Count
            ? sequence.Steps[failed].Id
            : null);
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
        {
            _runLog.UserFeedback(_sequence.Id, resolved);
            _usage.Feedback(_runId, _sequence.Id, resolved);
        }

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
