namespace Larmcentralen.Domain.Entities;

/// <summary>Links an alarm definition to a solution. A solution shared by several definitions has one row per definition.</summary>
public class DefinitionSolution
{
    public int AlarmDefinitionId { get; set; }
    public AlarmDefinition Definition { get; set; } = null!;

    public int SolutionId { get; set; }
    public Solution Solution { get; set; } = null!;
}
