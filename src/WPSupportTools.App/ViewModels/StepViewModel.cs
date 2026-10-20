using WPSupportTools.Core.Procedures;

namespace WPSupportTools.App.ViewModels;

public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Simulated,
    Skipped,
    Failed,
}

public sealed class StepViewModel(string procedureId, string name) : ObservableObject
{
    private StepStatus _status = StepStatus.Pending;
    private string? _message;

    public string ProcedureId { get; } = procedureId;

    public string Name { get; } = name;

    public StepStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void MarkRunning() => Status = StepStatus.Running;

    public void Apply(ProcedureResult result)
    {
        Status = result.Outcome switch
        {
            ProcedureOutcome.Success => StepStatus.Succeeded,
            ProcedureOutcome.NotConfigured => StepStatus.Simulated,
            ProcedureOutcome.Skipped or ProcedureOutcome.NotNeeded => StepStatus.Skipped,
            _ => StepStatus.Failed,
        };
        Message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message;
    }
}
