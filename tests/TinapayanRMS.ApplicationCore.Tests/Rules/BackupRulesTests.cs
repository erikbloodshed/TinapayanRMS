using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class BackupRulesTests
{
    private static readonly DateTime Morning = new(2026, 10, 3, 8, 15, 12);

    [Test]
    [Arguments(BackupKind.Manual, "TinapayanRMS_20261003_081512_manual.backup")]
    [Arguments(BackupKind.Automatic, "TinapayanRMS_20261003_081512_auto.backup")]
    [Arguments(BackupKind.BeforeRestore, "TinapayanRMS_20261003_081512_before-restore.backup")]
    [Arguments(BackupKind.CopiedIn, "TinapayanRMS_20261003_081512_copied-in.backup")]
    [Arguments(BackupKind.BeforeUpdate, "TinapayanRMS_20261003_081512_before-update.backup")]
    public void A_backup_is_named_by_database_time_and_kind_and_read_back(BackupKind kind, string fileName)
    {
        BackupRules.FileNameFor("TinapayanRMS", kind, Morning).Should().Be(fileName);

        BackupRules.TryParse(fileName, out var databaseName, out var parsedKind, out var takenAt).Should().BeTrue();
        databaseName.Should().Be("TinapayanRMS");
        parsedKind.Should().Be(kind);
        takenAt.Should().Be(Morning);
    }

    [Test]
    public void A_name_already_taken_moves_on_a_second_at_a_time_until_its_free_and_still_reads_back()
    {
        HashSet<string> taken =
        [
            "TinapayanRMS_20261003_081512_before-restore.backup",
            "TinapayanRMS_20261003_081513_before-restore.backup",
            "TinapayanRMS_20261003_081515_before-restore.backup",
        ];

        var name = BackupRules.FreeFileNameFor("TinapayanRMS", BackupKind.BeforeRestore, Morning, taken.Contains);

        name.Should().Be("TinapayanRMS_20261003_081514_before-restore.backup");
        BackupRules.TryParse(name, out _, out var kind, out var takenAt).Should().BeTrue();
        (kind, takenAt).Should().Be((BackupKind.BeforeRestore, Morning.AddSeconds(2)));

        BackupRules.FreeFileNameFor("TinapayanRMS", BackupKind.Manual, Morning, taken.Contains)
            .Should().Be("TinapayanRMS_20261003_081512_manual.backup", "another kind's file in the same second is a different name");
    }

    [Test]
    public void A_database_name_with_underscores_of_its_own_still_reads_back()
    {
        var fileName = BackupRules.FileNameFor("TinapayanRMS_Test_Backup_ab12cd34", BackupKind.Automatic, Morning);

        BackupRules.TryParse(fileName, out var databaseName, out var kind, out var takenAt).Should().BeTrue();
        databaseName.Should().Be("TinapayanRMS_Test_Backup_ab12cd34");
        kind.Should().Be(BackupKind.Automatic);
        takenAt.Should().Be(Morning);
    }

    [Test]
    [Arguments("TinapayanRMS.backup")]
    [Arguments("TinapayanRMS_before_CompositeProducts_20260929.backup")]
    [Arguments("TinapayanRMS_20261003_081512_weekly.backup")]
    [Arguments("TinapayanRMS_20261003_081512_manual.txt")]
    [Arguments("TinapayanRMS_20261340_081512_manual.backup")]
    public void A_name_the_app_didnt_give_isnt_read_as_one_it_did(string fileName)
    {
        BackupRules.TryParse(fileName, out _, out _, out _).Should().BeFalse();
    }

    [Test]
    public void The_daily_backup_is_due_until_one_is_taken_that_day()
    {
        var lastNight = Backup(BackupKind.Automatic, new DateTime(2026, 10, 2, 23, 59, 0));
        var manualToday = Backup(BackupKind.Manual, Morning);

        BackupRules.IsDailyBackupDue([], "TinapayanRMS", Morning).Should().BeTrue();
        BackupRules.IsDailyBackupDue([lastNight, manualToday], "TinapayanRMS", Morning).Should().BeTrue("a manual backup isn't the day's automatic one");
        BackupRules.IsDailyBackupDue([Backup(BackupKind.Automatic, new DateTime(2026, 10, 3, 0, 0, 1))], "TinapayanRMS", Morning).Should().BeFalse();
        BackupRules.IsDailyBackupDue([lastNight], "TinapayanRMS", new DateTime(2026, 10, 2, 23, 59, 30)).Should().BeFalse();
    }

    [Test]
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

    [Test]
    public void Another_databases_backups_in_the_folder_are_neither_counted_nor_deleted()
    {
        var demoToday = Backup(BackupKind.Automatic, Morning, "TinapayanRMS_Demo");
        var demoOld = Enumerable.Range(1, 20).Select(day => Backup(BackupKind.Automatic, new DateTime(2026, 9, day), "TinapayanRMS_Demo")).ToList();

        BackupRules.IsDailyBackupDue([demoToday], "TinapayanRMS", Morning).Should().BeTrue();
        BackupRules.AutomaticToDelete([.. demoOld, demoToday], "TinapayanRMS").Should().BeEmpty();
        BackupRules.AutomaticToDelete([.. demoOld, demoToday], "tinapayanrms_demo").Should().HaveCount(7, "a name is matched ignoring case");
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public void A_backups_listing_says_which_database_it_is_of_and_when_it_was_taken(string newLine)
    {
        // The start of what pg_restore --list printed for a backup of the dev database, on Windows and on a Mac.
        var listing = string.Join(newLine,
            ";",
            "; Archive created at 2026-10-11 00:06:41",
            ";     dbname: tinapayanrms",
            ";     TOC Entries: 219",
            ";     Compression: gzip",
            ";     Format: CUSTOM",
            ";     Dumped from database version: 18.6",
            ";",
            "; Selected TOC Entries:",
            ";",
            "4631; 3456 194084 COLLATION public case_insensitive tinapayanrms");

        BackupRules.TryReadArchiveListing(listing, out var header).Should().BeTrue();

        header.Should().Be(new BackupHeader("tinapayanrms", new DateTime(2026, 10, 11, 0, 6, 41)));
    }

    [Test]
    public void A_time_zone_after_the_time_is_left_out()
    {
        BackupRules.TryReadArchiveListing("; Archive created at 2026-10-03 08:15:12 PST\n;     dbname: shop", out var header).Should().BeTrue();

        header!.TakenAt.Should().Be(new DateTime(2026, 10, 3, 8, 15, 12));
    }

    [Test]
    [Arguments("")]
    [Arguments(";     dbname: tinapayanrms")]
    [Arguments("; Archive created at 2026-10-11 00:06:41")]
    [Arguments("; Archive created at yesterday\n;     dbname: tinapayanrms")]
    public void A_listing_without_both_isnt_read(string listing)
    {
        BackupRules.TryReadArchiveListing(listing, out var header).Should().BeFalse();
        header.Should().BeNull();
    }

    [Test]
    public void A_backup_from_an_earlier_version_needs_the_updates_it_lacks_in_order()
    {
        string[] known = ["01_Initial", "02_Packs", "03_Composites"];

        BackupRules.UpdatesNeeded(["01_Initial"], known).Should().Equal("02_Packs", "03_Composites");
        BackupRules.UpdatesNeeded(known, known).Should().BeEmpty();
    }

    [Test]
    public void A_backup_from_a_later_version_or_another_apps_is_refused()
    {
        string[] known = ["01_Initial", "02_Packs"];

        var later = () => BackupRules.UpdatesNeeded(["01_Initial", "02_Packs", "03_Composites"], known);
        var another = () => BackupRules.UpdatesNeeded([], known);

        later.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.LaterVersionText);
        another.Should().Throw<BusinessRuleException>().WithMessage(BackupRules.NotThisAppsText);
    }

    [Test]
    public void A_file_still_being_written_isnt_read_as_a_backup()
    {
        var name = BackupRules.FileNameFor("TinapayanRMS", BackupKind.Automatic, Morning) + BackupRules.PartialSuffix;

        BackupRules.TryParse(name, out _, out _, out _).Should().BeFalse();
    }

    [Test]
    public void The_newest_backup_is_this_databases_of_any_kind_but_one_copied_in()
    {
        var newest = Backup(BackupKind.Manual, Morning.AddDays(-1));
        List<DatabaseBackup> backups =
        [
            Backup(BackupKind.Automatic, Morning.AddDays(-3)),
            newest,
            Backup(BackupKind.CopiedIn, Morning),
            Backup(BackupKind.Manual, Morning, "TinapayanRMS_Copy"),
        ];

        BackupRules.Newest(backups, "tinapayanrms").Should().BeSameAs(newest);
        BackupRules.Newest([], "TinapayanRMS").Should().BeNull();
    }

    [Test]
    [Arguments(2.9, false)]
    [Arguments(3, true)]
    [Arguments(20, true)]
    public void A_backup_is_overdue_three_days_on(double daysOld, bool overdue)
    {
        BackupRules.IsOverdue(Backup(BackupKind.Automatic, Morning.AddDays(-daysOld)), Morning).Should().Be(overdue);
    }

    [Test]
    public void Having_no_backup_at_all_is_overdue()
    {
        BackupRules.IsOverdue(null, Morning).Should().BeTrue();
    }

    [Test]
    [Arguments(1, false)]
    [Arguments(6.9, false)]
    [Arguments(7, true)]
    [Arguments(30, true)]
    public void The_newest_backup_is_proved_again_a_week_after_the_last_proof(double daysSince, bool due)
    {
        var proved = Morning.AddDays(-daysSince);

        BackupRules.IsProofDue(proved, proved, Morning).Should().Be(due);
    }

    [Test]
    public void A_backup_never_proved_is_due()
    {
        BackupRules.IsProofDue(null, null, Morning).Should().BeTrue();
    }

    [Test]
    [Arguments(1, false)]
    [Arguments(23, false)]
    [Arguments(24, true)]
    public void A_proof_that_failed_is_tried_again_the_next_day_not_every_hour(int hoursSince, bool due)
    {
        BackupRules.IsProofDue(Morning.AddDays(-20), Morning.AddHours(-hoursSince), Morning).Should().Be(due);
    }

    private static DatabaseBackup Backup(BackupKind kind, DateTime takenAt, string databaseName = "TinapayanRMS") =>
        new($"{kind}-{takenAt:O}.backup", databaseName, kind, takenAt, 1024);
}
