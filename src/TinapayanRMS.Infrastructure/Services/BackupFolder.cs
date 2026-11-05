using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The backups folder, shared by every Windows user of the computer. The app keeps its backups in a folder under
/// ProgramData that it creates and gives an allow-Modify entry for Users, inherited by every file in it, so whoever uses the
/// app can take, copy, restore and delete them, whoever took them. pg_dump writes a backup as the user running the app.
/// Whoever creates the
/// folder owns it, and an owner may always change its permissions, so this needs no administrator.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BackupFolder
{
    private const InheritanceFlags ToFilesAndFolders = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    /// <summary>Creates <paramref name="folder"/> if it's missing, and adds the Users entry if it lacks it.</summary>
    public static void Ensure(string folder)
    {
        var directory = Directory.CreateDirectory(folder);
        var security = directory.GetAccessControl();
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        if (AllowsModify(security, users))
        {
            return;
        }

        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.Modify | FileSystemRights.Synchronize, ToFilesAndFolders, PropagationFlags.None, AccessControlType.Allow));
        try
        {
            directory.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Someone else's folder, without the entry: only its owner or an administrator can add it.
            throw new BusinessRuleException(
                $"The backups folder ({folder}) was made by another Windows user, without the access everyone needs. "
                + @"Ask an administrator to give BUILTIN\Users Modify on it.",
                ex);
        }
    }

    /// <summary>Whether <paramref name="account"/> may already modify the folder and everything made in it.</summary>
    private static bool AllowsModify(DirectorySecurity security, SecurityIdentifier account) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference.Equals(account)
                && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify
                && (rule.InheritanceFlags & ToFilesAndFolders) == ToFilesAndFolders
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0);
}
