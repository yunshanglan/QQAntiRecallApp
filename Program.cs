using System;
using System.Windows.Forms;

namespace QQAntiRecallApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            #if DEBUG
                        Logger.CurrentLevel = Logger.LogLevel.DEBUG;
            #else
                Logger.CurrentLevel = Logger.LogLevel.INFO;
            #endif
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}