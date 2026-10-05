using System.Diagnostics;
using System.IO;
using restByAno.Infrastructure.Configuration;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.Services;

/// <summary>
/// Runs storage bootstrap at startup and turns problems into themed, actionable dialogs.
/// Returns the settings to run with, or null when the app must exit. It never overwrites existing user files.
/// </summary>
public sealed class StartupCoordinator(IDialogService dialogs, StorageBootstrapper bootstrapper, StorageOptions options)
{
    public AppSettings? Run()
    {
        var result = bootstrapper.Run();
        while (true)
        {
            switch (result.Status)
            {
                case StorageStatus.Ready:
                    return result.Settings;

                case StorageStatus.Failed:
                    ShowFailure(result.Problem!);
                    return null;

                case StorageStatus.MissingDatabase:
                    var next = AskRecovery();
                    if (next is null) return null;
                    result = next;
                    break;
            }
        }
    }

    private StorageBootstrapResult? AskRecovery()
    {
        while (true)
        {
            var choice = dialogs.Show(new DialogRequest(
                "The restByAno database is missing",
                "restByAno was set up in this data folder before, but its database file is gone, so your collections and environments are not available. Nothing has been created or deleted. Choose how to continue.",
                [
                    new DialogButton("Restore from backup…", IsDefault: true),
                    new DialogButton("Create new database"),
                    new DialogButton("Exit", IsCancel: true),
                ])
            {
                Detail = options.DatabasePath,
            });

            switch (choice)
            {
                case 0:
                    var file = dialogs.PickOpenFile("Choose a restByAno backup", "restByAno database (*.db)|*.db|All files (*.*)|*.*", options.BackupDirectory);
                    if (file is null) continue;
                    var restored = bootstrapper.RestoreFromBackup(file);
                    if (restored.Status == StorageStatus.Failed)
                    {
                        dialogs.ShowMessage(restored.Problem!.Title, restored.Problem.Message);
                        continue;
                    }

                    return restored;

                case 1:
                    var confirm = dialogs.Show(new DialogRequest(
                        "Create an empty database?",
                        "A new, empty database will be created. Existing backups are not touched, and you can still restore one later by moving it back.",
                        [new DialogButton("Create empty database", IsDefault: true), new DialogButton("Back", IsCancel: true)]));
                    if (confirm != 0) continue;
                    var created = bootstrapper.CreateNewDatabase();
                    if (created.Status == StorageStatus.Failed)
                    {
                        dialogs.ShowMessage(created.Problem!.Title, created.Problem.Message);
                        continue;
                    }

                    return created;

                default:
                    return null;
            }
        }
    }

    private void ShowFailure(StorageProblem problem)
    {
        var buttons = new List<DialogButton>();
        if (Directory.Exists(options.DataDirectory))
            buttons.Add(new DialogButton("Open data folder"));
        buttons.Add(new DialogButton("Exit", IsDefault: true, IsCancel: true));

        var choice = dialogs.Show(new DialogRequest(problem.Title, problem.Message + " restByAno will now exit without changing any files.", buttons));
        if (buttons.Count == 2 && choice == 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{options.DataDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Opening Explorer is a convenience only.
            }
        }
    }
}
