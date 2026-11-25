using BepInEx.Logging;

namespace GroundControlRtsExpanded;

internal static class AddonLog
{
    private static ManualLogSource? logger;

    internal static void Initialize(ManualLogSource source)
    {
        logger = source;
    }

    internal static void Info(string message)
    {
        logger?.LogInfo(message);
    }

    internal static void Warn(string message)
    {
        logger?.LogWarning(message);
    }

    internal static void Error(string message)
    {
        logger?.LogError(message);
    }

    internal static void Debug(string message)
    {
        if (AddonConfig.VerboseLogging != null && AddonConfig.VerboseLogging.Value)
        {
            logger?.LogInfo("[DEBUG] " + message);
        }
    }
}
