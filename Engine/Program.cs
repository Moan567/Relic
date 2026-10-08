using Engine;
using Engine.Utils;

public class StartupDemo
{
    public static int Main(string[] args)
    {
        GameStartup.Run<MainEngine>(typeof(MainEngine).Assembly, args);
        return 0;
    }
}