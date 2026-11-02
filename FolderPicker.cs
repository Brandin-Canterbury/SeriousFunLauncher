using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SeriousFunLauncher
{
    /// <summary>
    /// The Explorer-style folder picker (address bar, search, Quick Access).
    /// </summary>
    /// <remarks>
    /// .NET Framework only ships the old tree-view FolderBrowserDialog; WPF's OpenFolderDialog
    /// needs .NET 8. This drives the Windows common item dialog directly in folder mode.
    /// </remarks>
    public static class FolderPicker
    {
        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
        public static string Show(Window owner, string title, string initialPath)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();

            try
            {
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);

                string start = NearestExistingFolder(initialPath);
                if (start != null && SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder) == 0)
                {
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }

                IntPtr hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                int hr = dialog.Show(hwnd);
                if (hr == ERROR_CANCELLED)
                {
                    return null;
                }

                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out IShellItem result);

                try
                {
                    result.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pathPtr);
                    try
                    {
                        return Marshal.PtrToStringUni(pathPtr);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pathPtr);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(result);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        /// <summary>The default install folders don't exist until first run, so start in their closest parent.</summary>
        private static string NearestExistingFolder(string path)
        {
            try
            {
                string current = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
                while (current != null && !Directory.Exists(current))
                {
                    current = Path.GetDirectoryName(current);
                }

                return current;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        // Vtable order matters: IModalWindow.Show, then IFileDialog, then IFileOpenDialog.
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
