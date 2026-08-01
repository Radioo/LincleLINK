namespace LincleLINK.Core.Storage;

/// <summary>
/// Thrown when a persisted instance manifest exists but cannot be deserialized.
/// The message includes the instance name and file path so the UI can show a
/// clear error instead of a raw JsonException.
/// </summary>
public sealed class InstanceStorageException : Exception
{
    public InstanceStorageException(string message)
        : base(message)
    {
    }

    public InstanceStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
