using System;
using MemeSearcher.Core.Jobs;

namespace MemeSearcher.ViewModels;

/// <summary>
/// A read-only snapshot of one <see cref="Job"/> at the moment JobsPanelViewModel last rebuilt its
/// list (#14). Job itself is a plain mutable class rather than an ObservableObject (see its own
/// doc comment), so this wrapper - not the Job - is what the view actually binds to.
/// </summary>
public class JobRowViewModel(Job job)
{
    public Guid Id { get; } = job.Id;

    public string Title { get; } = job.Title;

    public string KindDisplay { get; } = job.Kind.ToString();

    public JobState State { get; } = job.State;

    public string StateDisplay { get; } = job.State.ToString();

    public string? StatusMessage { get; } = job.StatusMessage;

    public bool HasStatusMessage { get; } = !string.IsNullOrEmpty(job.StatusMessage);

    public string? Error { get; } = job.Error;

    public bool HasError { get; } = !string.IsNullOrEmpty(job.Error);

    public bool IsCancellable { get; } = job.IsCancellable;
}
