namespace WinServerMonitor.Core.Domain;

/// <summary>What caused a run to be created.</summary>
public enum TaskTrigger
{
    Schedule = 0,
    Manual = 1,
    Rerun = 2,
    Retry = 3,
}
