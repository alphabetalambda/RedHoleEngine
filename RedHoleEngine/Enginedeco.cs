namespace RedHoleEngine;

public class Enginedeco
{
    public static void EngineTitlePrint()
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  _____          _ _    _       _        ______             _            \n |  __ \\        | | |  | |     | |      |  ____|           (_)           \n | |__) |___  __| | |__| | ___ | | ___  | |__   _ __   __ _ _ _ __   ___ \n |  _  // _ \\/ _` |  __  |/ _ \\| |/ _ \\ |  __| | '_ \\ / _` | | '_ \\ / _ \\\n | | \\ \\  __/ (_| | |  | | (_) | |  __/ | |____| | | | (_| | | | | |  __/\n |_|  \\_\\___|\\__,_|_|  |_|\\___/|_|\\___| |______|_| |_|\\__, |_|_| |_|\\___|\n                                                       __/ |             \n                                                      |___/              ");
        Console.ForegroundColor = originalColor;
    }
    public static void companylogoprint()
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        ConsoleColor backgroundColor = Console.BackgroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        
        Console.WriteLine(" _____ ___                                        _   _         \n|   | |_  |___ ___    ___ ___ ___ ___ ___ ___ ___| |_|_|___ ___ \n| | | | | |  _| . |  |  _| . |  _| . | . |  _| .'|  _| | . |   |\n|_|___| |_|___|___|  |___|___|_| |  _|___|_| |__,|_| |_|___|_|_|\n                                 |_|                            ");
        Console.BackgroundColor = backgroundColor;
        Console.ForegroundColor = originalColor;
    }
}