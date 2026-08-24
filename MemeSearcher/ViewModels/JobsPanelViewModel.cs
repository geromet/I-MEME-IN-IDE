using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Jobs;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Backs the shell's bottom "Jobs / Errors" panel (Milestone 12's shell, Milestone 14's content -
/// #14, addendum §27/§28, handoff §28). Rebuilds its row list wholesale on every
/// <see cref="IJobQueue.Changed"/> notification rather than diffing - the same "coarse event,
/// rebuild everything" pattern LibraryViewModel.LoadAsync already uses - since job lists are small
/// and this keeps a background-thread-raised event trivially safe to fold into a UI collection
/// once marshalled.
/// </summary>
public partial class JobsPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IJobQueue jobQueue;

    public ObservableCollection<JobRowViewModel> Jobs { get; } = [];

    [ObservableProperty]
    private bool _hasJobs;

    public JobsPanelViewModel(IJobQueue jobQueue)
    {
        this.jobQueue = jobQueue;
        jobQueue.Changed += OnJobQueueChanged;
        Rebuild();
    }

    private void OnJobQueueChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        Jobs.Clear();
        foreach (var job in jobQueue.Jobs)
        {
            Jobs.Add(new JobRowViewModel(job));
        }

        HasJobs = Jobs.Count > 0;
    }

    [RelayCommand]
    private void Cancel(JobRowViewModel row) => jobQueue.Cancel(row.Id);

    public void Dispose() => jobQueue.Changed -= OnJobQueueChanged;
}
