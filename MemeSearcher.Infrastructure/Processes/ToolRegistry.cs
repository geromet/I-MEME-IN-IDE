using System.Collections.Generic;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Plain holder for whatever locators App.axaml.cs's ConfigureServices already constructed (#16) - see IToolRegistry's own doc comment for why this exists.</summary>
public class ToolRegistry(IReadOnlyList<IExternalToolLocator> locators) : IToolRegistry
{
    public IReadOnlyList<IExternalToolLocator> Locators { get; } = locators;
}
