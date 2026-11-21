using Larmcentralen.Client.Core.Sync;
using Microsoft.AspNetCore.Components;

namespace Larmcentralen.Client.UI.Components;

/// <summary>
/// Base for components that show data from the local copy. Re-reads and re-renders
/// whenever a sync finishes or the connection changes.
/// </summary>
public abstract class KnowledgeBaseComponent : ComponentBase, IDisposable
{
    [Inject] protected KnowledgeBase Kb { get; set; } = null!;

    protected bool IsOnline => Kb.IsOnline;

    /// <summary>Admins can edit; everyone else only reads. Hide edit features when false.</summary>
    protected bool CanEdit => Kb.CanEdit;

    protected override void OnInitialized()
    {
        Kb.Changed += OnKbChanged;
        Load();
    }

    /// <summary>Read what the component shows from <see cref="KnowledgeBase.Index"/>.</summary>
    protected virtual void Load() { }

    private void OnKbChanged(object? sender, EventArgs e) => _ = InvokeAsync(() =>
    {
        Load();
        StateHasChanged();
    });

    public virtual void Dispose()
    {
        Kb.Changed -= OnKbChanged;
        GC.SuppressFinalize(this);
    }
}
