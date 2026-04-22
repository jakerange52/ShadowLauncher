namespace ShadowLauncher.Core.Exceptions;

public class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
