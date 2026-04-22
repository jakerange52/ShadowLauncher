namespace ShadowLauncher.Core.Exceptions;

public class LaunchException(string message, Exception? innerException = null)
    : Exception(message, innerException);
