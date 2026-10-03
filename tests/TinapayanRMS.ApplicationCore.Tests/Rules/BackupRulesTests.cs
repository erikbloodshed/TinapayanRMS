using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class BackupRulesTests
{
    private static readonly DateTime Morning = new(2026, 10, 3, 8, 15, 12);

    [Theory]
    [InlineData(BackupKind.Manual, "TinapayanRMS_20261003_081512_manual.bak")]
    [InlineData(BackupKind.Automatic, "TinapayanRMS_20261003_081512_auto.bak")]
    [InlineData(BackupKind.BeforeRestore, "TinapayanRMS_20261003_081512_before-restore.bak")]
    [InlineData(BackupKind.CopiedIn, "TinapayanRMS_20261003_081512_copied-in.bak")]
    public void A_backup_is_named_by_database_time_and_kind_and_read_back(BackupKind kind, string fileName)
    {
        BackupRules.FileNameFor("TinapayanRMS", kind, Morning).Should().Be(fileName);

        BackupRules.TryParse(fileName, out var databaseName, out var parsedKind, out var takenAt).Should().BeTrue();
        databaseName.Should().Be("TinapayanRMS");
        parsedKind.Should().Be(kind);
        takenAt.Should().Be(Morning);
    }

    [Fact]
    public void A_database_name_with_underscores_of_its_own_still_reads_back()
    {
        var fileName = BackupRules.FileNameFor("TinapayanRMS_Test_Backup_ab12cd34", BackupKind.Automatic, Morning);

        BackupRules.TryParse(fileName, out var databaseName, out var kind, out var takenAt).Should().BeTrue();
        databaseName.Should().Be("TinapayanRMS_Test_Backup_ab12cd34");
        kind.Should().Be(BackupKind.Automatic);
        takenAt.Should().Be(Morning);
    }

    [Theory]
    [InlineData("TinapayanRMS.bak")]
    [InlineData("TinapayanRMS_before_CompositeProducts_20260929.bak")]
    [InlineData("TinapayanRMS_20261003_081512_weekly.bak")]
    [InlineData("TinapayanRMS_20261003_081512_manual.txt")]
    [InlineData("TinapayanRMS_20261340_081512_manual.bak")]
    public void A_name_the_app_didnt_give_isnt_read_as_one_it_did(string fileName)
    {
        BackupRules.TryParse(fileName, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void The_daily_backup_is_due_until_one_is_taken_that_day()
    {
        var lastNight = Backup(BackupKind.Automatic, new DateTime(2026, 10, 2, 23, 59, 0));
        var manualToday = Backup(BackupKind.Manual, Morning);

        BackupRules.IsDailyBackupDue([], "TinapayanRMS", Morning).Should().BeTrue();
        BackupRules.IsDailyBackupDue([lastNight, manualToday], "TinapayanRMS", Morning).Should().BeTrue("a manual backup isn't the day's automatic one");
        BackupRules.IsDailyBackupDue([Backup(BackupKind.Automatic, new DateTime(2026, 10, 3, 0, 0, 1))], "TinapayanRMS", Morning).Should().BeFalse();
        BackupRules.IsDailyBackupDue([lastNight], "TinapayanRMS", new DateTime(2026, 10, 2, 23, 59, 30)).Should().BeFalse();
    }

    [Fact]
    public void Only_the_automatic_backups_beyond_the_newest_fourteen_are_deleted()
    {
        var automatic = Enumerable.Range(1, 16).Select(day => Backup(BackupKind.Automatic, new DateTime(2026, 9, day, 8, 0, 0))).ToList();
        var others = new[]
        {
            Backup(BackupKind.Manual, new DateTime(2026, 8, 1)),
            Backup(BackupKind.BeforeRestore, new DateTime(2026, 8, 2)),
            Backup(BackupKind.CopiedIn, new DateTime(2026, 8, 3)),
        };

        var deleted = BackupRules.AutomaticToDelete([.. automatic, .. others], "TinapayanRMS");

        deleted.Select(b => b.TakenAt.Day).Should().Equal(2, 1);
    }

    [Fact]
    public void Another_databases_backups_in_the_folder_are_neither_counted_nor_deleted()
    {
        var demoToday = Backup(BackupKind.Automatic, Morning, "TinapayanRMS_Demo");
        var demoOld = Enumerable.Range(1, 20).Select(day => Backup(BackupKind.Automatic, new DateTime(2026, 9, day), "TinapayanRMS_Demo")).ToList();

        BackupRules.IsDailyBackupDue([demoToday], "TinapayanRMS", Morning).Should().BeTrue();
        BackupRules.AutomaticToDelete([.. demoOld, demoToday], "TinapayanRMS").Should().BeEmpty();
        BackupRules.AutomaticToDelete([.. demoOld, demoToday], "tinapayanrms_demo").Should().HaveCount(7, "a name is matched as SQL Server matches it, ignoring case");
    }

    [Theory]
    [InlineData("SQLEXPRESS", @"NT SERVICE\MSSQL$SQLEXPRESS")]
    [InlineData(null, @"NT SERVICE\MSSQLSERVER")]
    [InlineData("", @"NT SERVICE\MSSQLSERVER")]
    [InlineData("MSSQLSERVER", @"NT SERVICE\MSSQLSERVER")]
    public void SQL_Server_writes_files_as_its_service_account(string? instanceName, string account)
    {
        BackupRules.ServiceAccountFor(instanceName).Should().Be(account);
    }

    [Fact]
    public void A_database_on_another_computer_is_refused()
    {
        var local = () => BackupRules.EnsureLocalServer("TFB-Operations", "TFB-OPERATIONS");
        var remote = () => BackupRules.EnsureLocalServer("OFFICE-PC", "TFB-OPERATIONS");

        local.Should().NotThrow();
        remote.Should().Throw<BusinessRuleException>().WithMessage("*on OFFICE-PC*");
    }

    [Fact]
    public void A_backups_files_go_onto_this_databases()
    {
        var moves = BackupRules.RestoreMoves(
            [("TinapayanRMS_Demo_log", true), ("TinapayanRMS_Demo", false)],
            [(@"D:\Data\TinapayanRMS.mdf", false), (@"D:\Data\TinapayanRMS_log.ldf", true)]);

        moves.Should().Equal(("TinapayanRMS_Demo", @"D:\Data\TinapayanRMS.mdf"), ("TinapayanRMS_Demo_log", @"D:\Data\TinapayanRMS_log.ldf"));
    }

    [Fact]
    public void A_backup_or_database_with_another_layout_is_refused()
    {
        var twoDataFiles = () => BackupRules.RestoreMoves(
            [("Data", false), ("Data2", false), ("Log", true)],
            [(@"D:\a.mdf", false), (@"D:\a.ldf", true)]);
        var noDatabase = () => BackupRules.RestoreMoves([("Data", false), ("Log", true)], []);

        twoDataFiles.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.FilesDontMatchText);
        noDatabase.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.FilesDontMatchText);
    }

    [Fact]
    public void A_backup_from_an_earlier_version_needs_the_updates_it_lacks_in_order()
    {
        string[] known = ["01_Initial", "02_Packs", "03_Composites"];

        BackupRules.UpdatesNeeded(["01_Initial"], known).Should().Equal("02_Packs", "03_Composites");
        BackupRules.UpdatesNeeded(known, known).Should().BeEmpty();
    }

    [Fact]
    public void A_backup_from_a_later_version_or_another_apps_is_refused()
    {
        string[] known = ["01_Initial", "02_Packs"];

        var later = () => BackupRules.UpdatesNeeded(["01_Initial", "02_Packs", "03_Composites"], known);
        var another = () => BackupRules.UpdatesNeeded([], known);

        later.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.LaterVersionText);
        another.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.NotThisAppsText);
    }

    private static DatabaseBackup Backup(BackupKind kind, DateTime takenAt, string databaseName = "TinapayanRMS") =>
        new($"{kind}-{takenAt:O}.bak", databaseName, kind, takenAt, 1024);
}
