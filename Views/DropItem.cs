using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

public enum DropItemState
{
    Ready,
    Invalid,
    Working,
    Done,
    Warning,
    Failed,
}

/// <summary>One dropped file as shown in the confirm/results dialog.</summary>
public sealed class DropItem : INotifyPropertyChanged
{
    public DropItem(ResolvedProgram program)
    {
        Program = program;
        Icon = IconHelper.Get(program.ExePath ?? program.Source);
        if (program.IsValid)
            Set(DropItemState.Ready, "Ready to add");
        else
            Set(DropItemState.Invalid, program.Error ?? "Can't be added.");
    }

    public ResolvedProgram Program { get; }
    public ImageSource? Icon { get; }
    public string Name => Program.DisplayName;
    public string Detail => Program.ExePath ?? Program.Source;

    public DropItemState State { get; private set; }
    public string StatusText { get; private set; } = "";

    public string StatusGlyph => State switch
    {
        DropItemState.Ready => "",    // check mark
        DropItemState.Working => "", // sync
        DropItemState.Done => "",    // completed
        DropItemState.Warning => "", // warning
        _ => "",                     // error badge
    };

    public void SetWorking() => Set(DropItemState.Working, "Adding…");

    public void SetOutcome(ApplyOutcome outcome) => Set(outcome.Status switch
    {
        ApplyStatus.Done => DropItemState.Done,
        ApplyStatus.Failed => DropItemState.Failed,
        _ => DropItemState.Warning,
    }, outcome.Message);

    private void Set(DropItemState state, string text)
    {
        State = state;
        StatusText = text;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
