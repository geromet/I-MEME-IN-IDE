using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Library;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Milestone 17 (#20): named, saved, curated subsets of the corpus - the durable counterpart to
/// #13's ad-hoc checkbox selection. Static membership only for now, per #20's explicit "ship static
/// catalogs first" - no predicate/smart-catalog machinery here.
/// </summary>
public partial class CatalogsViewModel(
    CatalogService catalogService, LibraryService libraryService, LibraryViewModel libraryViewModel) : ViewModelBase
{
    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _statusMessage = "No catalogs yet.";

    partial void OnStatusMessageChanged(string value) => IsStatusError = false;

    private void SetError(string message)
    {
        StatusMessage = message;
        IsStatusError = true;
    }

    public ObservableCollection<CatalogRowViewModel> Catalogs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCatalog))]
    private CatalogRowViewModel? _selectedCatalog;

    public bool HasSelectedCatalog => SelectedCatalog is not null;

    /// <summary>Every library media item with a checkbox for membership in <see cref="SelectedCatalog"/>. Empty when no catalog is selected.</summary>
    public ObservableCollection<CatalogMemberRowViewModel> Members { get; } = [];

    [ObservableProperty]
    private string _newCatalogName = "";

    [ObservableProperty]
    private string _newCatalogDescription = "";

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var summaries = await catalogService.GetAllAsync();
            var previouslySelectedId = SelectedCatalog?.Id;

            Catalogs.Clear();
            foreach (var summary in summaries)
            {
                Catalogs.Add(new CatalogRowViewModel(summary));
            }

            SelectedCatalog = Catalogs.FirstOrDefault(c => c.Id == previouslySelectedId);
            StatusMessage = Catalogs.Count > 0 ? $"{Catalogs.Count} catalog(s)." : "No catalogs yet.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load catalogs: {ex.Message}");
        }
    }

    partial void OnSelectedCatalogChanged(CatalogRowViewModel? value) => _ = LoadMembersAsync();

    private async Task LoadMembersAsync()
    {
        foreach (var row in Members)
        {
            row.PropertyChanged -= OnMemberRowPropertyChanged;
        }

        Members.Clear();

        if (SelectedCatalog is null)
        {
            return;
        }

        var allMedia = await libraryService.GetAllAsync();
        var memberIds = await catalogService.GetMemberIdsAsync(SelectedCatalog.Id);

        foreach (var media in allMedia)
        {
            var row = new CatalogMemberRowViewModel(media.Id, media.Title, memberIds.Contains(media.Id));
            row.PropertyChanged += OnMemberRowPropertyChanged;
            Members.Add(row);
        }
    }

    private void OnMemberRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CatalogMemberRowViewModel.IsMember)
            || sender is not CatalogMemberRowViewModel row
            || SelectedCatalog is null)
        {
            return;
        }

        _ = PersistMembershipAsync(SelectedCatalog.Id, row);
    }

    private async Task PersistMembershipAsync(Guid catalogId, CatalogMemberRowViewModel row)
    {
        await catalogService.SetMemberAsync(catalogId, row.MediaId, row.IsMember);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = NewCatalogName.Trim();
        if (name.Length == 0)
        {
            SetError("Enter a name for the catalog.");
            return;
        }

        var description = NewCatalogDescription.Trim();
        await catalogService.CreateAsync(name, description.Length == 0 ? null : description);

        NewCatalogName = "";
        NewCatalogDescription = "";
        await LoadAsync();
        StatusMessage = $"Created catalog \"{name}\".";
    }

    /// <summary>First click arms the confirmation; a second click on the same row deletes for real - same pattern as LibraryViewModel.DeleteSourceFileAsync.</summary>
    [RelayCommand]
    private async Task DeleteAsync(CatalogRowViewModel catalog)
    {
        if (!catalog.IsPendingDelete)
        {
            catalog.IsPendingDelete = true;
            return;
        }

        await catalogService.DeleteAsync(catalog.Id);
        if (SelectedCatalog?.Id == catalog.Id)
        {
            SelectedCatalog = null;
        }

        await LoadAsync();
        StatusMessage = $"Deleted catalog \"{catalog.Name}\". Its sources were kept in the library.";
    }

    [RelayCommand]
    private void CancelDelete(CatalogRowViewModel catalog) => catalog.IsPendingDelete = false;

    [RelayCommand]
    private void BeginRename(CatalogRowViewModel catalog)
    {
        catalog.EditName = catalog.Name;
        catalog.EditDescription = catalog.Description ?? "";
        catalog.IsEditing = true;
    }

    [RelayCommand]
    private void CancelRename(CatalogRowViewModel catalog) => catalog.IsEditing = false;

    [RelayCommand]
    private async Task SaveRenameAsync(CatalogRowViewModel catalog)
    {
        var name = catalog.EditName.Trim();
        if (name.Length == 0)
        {
            SetError("Enter a name for the catalog.");
            return;
        }

        var description = catalog.EditDescription.Trim();
        await catalogService.RenameAsync(catalog.Id, name, description.Length == 0 ? null : description);

        await LoadAsync();
        StatusMessage = $"Renamed catalog to \"{name}\".";
    }

    /// <summary>
    /// "Select a catalog as the active search scope" (#20) - bulk-applies this catalog's membership
    /// onto Media.IsSelectedForSearch, then reloads the shared LibraryViewModel so every open search
    /// tab picks up the new scope the same way it already does for manual checkbox edits
    /// (MainWindowViewModel watches LibraryViewModel.SelectionSummary for exactly this).
    /// </summary>
    [RelayCommand]
    private async Task ApplyToSearchAsync(CatalogRowViewModel catalog)
    {
        var memberIds = await catalogService.GetMemberIdsAsync(catalog.Id);
        await libraryService.ApplyCatalogScopeAsync(memberIds, catalog.Name);
        await libraryViewModel.LoadAsync();
        StatusMessage = $"Applied \"{catalog.Name}\" ({memberIds.Count} source(s)) as the active search scope.";
    }
}
