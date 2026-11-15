using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.Services
{
    public interface IStandardDialogs
    {
        SaveDialogResult DisplaySaveDialog(IPackFileService pfs, List<string> extensions);
        /// <summary>The save dialog with its file name box starting at <paramref name="initialPath"/>, a full pack path.</summary>
        SaveDialogResult DisplaySaveDialog(IPackFileService pfs, List<string> extensions, string initialPath);
        BrowseDialogResultFile DisplayBrowseDialog(List<string> extensions);

        /// <summary>Only shows files whose pack path also passes <paramref name="pathFilter"/>. The
        /// extension match alone is a substring match, so ".dat" also shows ".data" files.</summary>
        BrowseDialogResultFile DisplayBrowseDialog(List<string> extensions, Func<string, bool>? pathFilter);
        BrowseDialogResultFolder DisplayBrowseFolderDialog();
        SystemOpenFileDialogResult ShowSystemOpenFileDialog(bool multiselect = false, string filter = "All files|*.*");
        SystemSaveFileDialogResult ShowSystemSaveFileDialog(string initialFileName, string filter, string defaultExt);
        SystemBrowseFolderDialogResult ShowSystemFolderBrowserDialog();
        string ShowFolderNameDialog(IEnumerable<string> existingNames, string currentValue = "");

        void ShowExceptionWindow(Exception e, string userInfo = "");
        void ShowErrorViewDialog(string title, ErrorList errorItems, bool modal = true);

        TextInputDialogResult ShowTextInputDialog(string title, string initialText = "");
        void ShowDialogBox(string message, string title = "Error");
        ShowMessageBoxResult ShowYesNoBox(string message, string title);
        /// <summary>A message with one button per choice: the index of the one picked, or null when the window is closed. The first
        /// is the default and the last answers Escape.</summary>
        int? ShowChoiceBox(string message, string title, IReadOnlyList<string> choices);
        IWaitCursor ShowWaitCursor();
    }

    public record SaveDialogResult(bool Result, PackFile? SelectedPackFile, string? SelectedFilePath);
    public record BrowseDialogResultFile(bool Result, PackFile File);
    public record BrowseDialogResultFolder(bool Result, string Folder);
    public record SystemOpenFileDialogResult(bool Result, IReadOnlyList<string> FilePaths);
    public record SystemSaveFileDialogResult(bool Result, string? FilePath);
    public record SystemBrowseFolderDialogResult(bool Result, string? FolderPath);
    public record TextInputDialogResult(bool Result, string Text);

    public enum ShowMessageBoxResult
    {
        OK,
        Cancel,
    }

    public interface IWaitCursor : IDisposable{}
}
