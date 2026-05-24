using GEffectsLogic.Logging;

namespace KSAGEffects.Logging
{
    public class LogicLogging : Logger
    {
        public const string LogPrefix = "[GEffectsLogicInstance] ";

        public override bool LogStr(string message, int id, LogLevel level = LogLevel.Debug)
        {
            string name = KSAGEffectsLogicInstance.GetInstanceName(id);

            switch (level)
            {
                case LogLevel.Debug:
                    if (GEffectsLogic.LogicSettings.DebugMode)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"{LogPrefix}Debug ({name}): {message}");
                    }
                    break;
                case LogLevel.Info:
                    if (!GEffectsLogic.LogicSettings.SuppresInfoLogs)
                        Console.WriteLine($"{LogPrefix}Info ({name}): {message}");
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{LogPrefix}Warning ({name}): {message}");
                    break;
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"{LogPrefix}Error ({name}): {message}");
                    break;
                default:
                    Console.WriteLine($"{LogPrefix}Unknown LogLevel ({name}): {message}");
                    break;
            }
            Console.ResetColor();
            return true;
        }

        public LogicLogging()
        {
            Instance = this;
        }
    }
}
