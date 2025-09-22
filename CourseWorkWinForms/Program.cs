using Bittorrent;
using TorrentClientUI;

namespace CourseWorkWinForms
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.Run(new MainForm());
		}
    }
}