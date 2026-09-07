using System.Windows.Forms;

namespace FunnyGame.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new GameForm();
        Application.Run(form);
    }
}
