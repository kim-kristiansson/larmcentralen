namespace Larmcentralen.Domain.Entities;

/// <summary>Links an alarm to a solution. A solution shared by several units has one row per alarm.</summary>
public class AlarmSolution
{
    public int AlarmId { get; set; }
    public Alarm Alarm { get; set; } = null!;

    public int SolutionId { get; set; }
    public Solution Solution { get; set; } = null!;
}
