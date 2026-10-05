using System.IO;
using System.Windows;
using restByAno.Desktop.Views;
using Microsoft.Win32;

namespace restByAno.Desktop.Services;

public sealed record DialogButton(string Label, bool IsDefault = false, bool IsCancel = false);

public sealed record DialogRequest(string Title, string Message, IReadOnlyList<DialogButton> Buttons)
{
    /// <summary>Optional monospace, selectable text such as a file path.</summary>
    public string? Detail { get; init; }

    /// <summary>When not null, the dialog shows a text box with this initial text; non-cancel buttons need non-blank text.</summary>
    public string? InputText { get; init; }

    /// <summary>When not null, the dialog shows a list; non-cancel buttons need a selected entry.</summary>
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>What the user entered or picked in a dialog, plus which button closed it.</summary>
public sealed record DialogOutcome(int ButtonIndex, string? InputText, int SelectedChoice);

public interface IDialogService
{
    /// <summary>Shows a themed dialog and returns the index of the chosen button (the cancel button when dismissed).</summary>
    int Show(DialogRequest request);

    DialogOutcome ShowDetailed(DialogRequest request);

    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    string? PromptText(string title, string message, string initialText, string okLabel = "OK");

    /// <summary>Asks the user to pick one entry. Returns its index, or null when cancelled.</summary>
    int? PickChoice(string title, string message, IReadOnlyList<string> choices, string okLabel = "OK");

    void ShowMessage(string title, string message, string? detail = null);

    /// <summary>Native Open File dialog (OS-controlled appearance). Returns null when cancelled.</summary>
    string? PickOpenFile(string title, string filter, string? initialDirectory);

    /// <summary>Native Save File dialog (OS-controlled appearance). Returns null when cancelled; asks before replacing an existing file.</summary>
    string? PickSaveFile(string title, string filter, string defaultFileName);
}

public sealed class DialogService : IDialogService
{
    public int Show(DialogRequest request) => ShowDetailed(request).ButtonIndex;

    public DialogOutcome ShowDetailed(DialogRequest request)
    {
        var dialog = new DialogWindow(request);
        if (Application.Current?.MainWindow is { IsVisible: true } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true; // no main window yet (startup): keep the dialog reachable
        }

        dialog.ShowDialog();
        return new DialogOutcome(dialog.ChosenIndex, dialog.InputValue, dialog.SelectedChoice);
    }

    public string? PromptText(string title, string message, string initialText, string okLabel = "OK")
    {
        var outcome = ShowDetailed(new DialogRequest(title, message,
            [new DialogButton(okLabel, IsDefault: true), new DialogButton("Cancel", IsCancel: true)]) { InputText = initialText });
        return outcome.ButtonIndex == 0 ? outcome.InputText?.Trim() : null;
    }

    public int? PickChoice(string title, string message, IReadOnlyList<string> choices, string okLabel = "OK")
    {
        var outcome = ShowDetailed(new DialogRequest(title, message,
            [new DialogButton(okLabel, IsDefault: true), new DialogButton("Cancel", IsCancel: true)]) { Choices = choices });
        return outcome.ButtonIndex == 0 && outcome.SelectedChoice >= 0 ? outcome.SelectedChoice : null;
    }

    public void ShowMessage(string title, string message, string? detail = null) =>
        Show(new DialogRequest(title, message, [new DialogButton("OK", IsDefault: true, IsCancel: true)]) { Detail = detail });

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName, OverwritePrompt = true, AddExtension = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string title, string filter, string? initialDirectory)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
