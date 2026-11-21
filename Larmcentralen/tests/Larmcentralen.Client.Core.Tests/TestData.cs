using Larmcentralen.Client.Core.Models;

namespace Larmcentralen.Client.Core.Tests;

/// <summary>A small slice of the OCSS alarm list, shaped like the real data.</summary>
internal static class TestData
{
    private static readonly DateTime Created = new(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc);

    public static SyncSnapshot Snapshot() => new(
        Created,
        [
            new Area(1, "OCSS", "T10-linjen", null),
            new Area(2, "RU1", null, null),
        ],
        [
            new Equipment(10, "T101", "Härdugn 1", 1, null),
            new Equipment(11, "W2", "Tvätt 2", 1, null),
            new Equipment(12, "Unilader", "Laddvagn", 1, null),
            new Equipment(20, "MAIN", "Styrsystem", 2, null),
        ],
        [
            new Alarm(100, "Temperatur för hög", "M200.4", Severity.High, "Kontrollera termoelement", 10, Created, null),
            new Alarm(101, "Ingen svarssignal tändbrännare", "M206.6", Severity.Critical, null, 10, Created, null),
            new Alarm(102, "Säkringsautomater utlösta", "M201.0", Severity.Medium, null, 11, Created, null),
            new Alarm(103, "Temperatur tvättbadets nivå låg", "M202.1", Severity.Low, "Kolla värmen", 11, Created, null),
            new Alarm(104, "Nödstopp aktiverat", "M300.0", Severity.Critical, null, 12, Created, null),
            // Same code on another machine is a different alarm.
            new Alarm(105, "Nödstopp aktiverat", "M300.0", Severity.Critical, null, 20, Created, null),
            // Orphan: equipment missing, must not show up.
            new Alarm(106, "Spökarm", "X1", Severity.Low, null, 999, Created, null),
        ],
        [
            new Solution(1000, "Byt termoelement", "<p>Steg 1: <strong>stäng av</strong> ugnen.</p>", 20, "30 min", 100, Created, null),
            new Solution(1001, "Kontrollera regulatorn", "<ol><li>Öppna skåpet</li></ol>", 10, null, 100, Created, null),
        ]);
}
