namespace Larmcentralen.Application.DTOs;

/// <summary>Outcome of a create, change or delete: the item on success, otherwise why not (shown to the user).</summary>
public record WriteResult<T>(WriteStatus Status, T? Value = default, string? Error = null)
{
    public static WriteResult<T> Ok(T value) => new(WriteStatus.Ok, value);
    public static WriteResult<T> NotFound() => new(WriteStatus.NotFound);
    public static WriteResult<T> Invalid(string error) => new(WriteStatus.Invalid, Error: error);
    public static WriteResult<T> Conflict(string error) => new(WriteStatus.Conflict, Error: error);
}
