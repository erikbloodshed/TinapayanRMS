using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The backups folder, set up so SQL Server can write to it (2026-10-03). SQL Server writes a backup file as its own
/// service account, not as the user, and that account has no access to a user's own folders, so the app keeps its
/// backups in a folder under ProgramData that it creates and gives two allow-Modify entries, inherited by every file in
/// it: the service's account, to write and read backups, and Users, so whoever uses the app can copy and delete them.
/// Whoever creates the folder owns it, and an owner may always change its permissions, so this needs no administrator.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BackupFolder
{
    private const InheritanceFlags ToFilesAndFolders = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    /// <summary>Creates <paramref name="folder"/> if it's missing, and adds whichever of the two entries it lacks.</summary>
    public static void Ensure(string folder, string serviceAccount)
    {
        var directory = Directory.CreateDirectory(folder);
        var security = directory.GetAccessControl();
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        SecurityIdentifier service;
        try
        {
            service = (SecurityIdentifier)new NTAccount(serviceAccount).Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException ex)
        {
            throw new BusinessRuleException($"SQL Server's account ({serviceAccount}) isn't on this computer, so it can't be given the backups folder.", ex);
        }

        var missing = new[] { service, users }.Where(account => !AllowsModify(security, account)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var account in missing)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                account, FileSystemRights.Modify | FileSystemRights.Synchronize, ToFilesAndFolders, PropagationFlags.None, AccessControlType.Allow));
        }

        try
        {
            directory.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Someone else's folder, without the entries: only its owner or an administrator can add them.
            throw new BusinessRuleException(
                $"The backups folder ({folder}) was made by another Windows user, without the access SQL Server needs. "
                + $@"Ask an administrator to give {serviceAccount} and BUILTIN\Users Modify on it.",
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
