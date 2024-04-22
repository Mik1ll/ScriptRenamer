using System;

namespace ScriptRenamer;

public class ScriptRenamerException : Exception
{
    public ScriptRenamerException()
    {
    }

    public ScriptRenamerException(string message) : base(message)
    {
    }

    public ScriptRenamerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
