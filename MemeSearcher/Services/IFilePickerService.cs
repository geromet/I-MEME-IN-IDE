using System.Collections.Generic;
using System.Threading.Tasks;

namespace MemeSearcher.Services;

/// <summary>
/// Isolates the ViewModel from Avalonia's TopLevel/StorageProvider so LibraryViewModel stays
/// testable without a live windowing system (handoff §3: UI must sit behind services, not be
/// reached into directly).
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Lets the user pick a transcript file and, optionally, a companion audio/video file for the
    /// same media item (addendum §7: transcript and media filenames don't have to match, and both
    /// can be selected together). Returns the files as picked, unclassified - the caller sorts out
    /// which is which by extension.
    /// </summary>
    Task<IReadOnlyList<string>> PickMediaFilesAsync();
}
