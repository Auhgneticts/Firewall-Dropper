using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FirewallDropper.Services;

/// <summary>Reads and writes Windows .lnk shortcuts through the shell's IShellLink interface.</summary>
public static class ShellLink
{
    public static string? GetTarget(string lnkPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0 /* STGM_READ */);
            var sb = new StringBuilder(32768);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var target = sb.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static void Create(string lnkPath, string targetPath, string? description = null, string? arguments = null)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
            link.SetIconLocation(targetPath, 0);
            if (description is not null) link.SetDescription(description);
            if (arguments is not null) link.SetArguments(arguments);
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// Shortcuts created by Windows Installer ("advertised" shortcuts, e.g. Microsoft Office)
    /// store a product/component id instead of a path. Ask MSI where the program really is.
    /// </summary>
    public static string? GetAdvertisedTarget(string lnkPath)
    {
        try
        {
            var product = new StringBuilder(39);
            var feature = new StringBuilder(39);
            var component = new StringBuilder(39);
            if (MsiGetShortcutTarget(lnkPath, product, feature, component) != 0)
                return null;

            uint len = 32768;
            var path = new StringBuilder((int)len);
            int state = MsiGetComponentPath(product.ToString(), component.ToString(), path, ref len);
            const int InstallStateLocal = 3, InstallStateSource = 4;
            return state is InstallStateLocal or InstallStateSource && path.Length > 0 ? path.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiGetShortcutTarget(string szShortcutTarget, StringBuilder szProductCode,
        StringBuilder szFeatureId, StringBuilder szComponentCode);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiGetComponentPath(string szProduct, string szComponent, StringBuilder lpPathBuf, ref uint pcchBuf);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
