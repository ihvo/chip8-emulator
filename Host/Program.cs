using System;
using System.IO;
using System.Windows.Forms;

namespace Chip8.Host
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var emulator = new Emulator();
            emulator.Reset();

            using (var rom = GetRomContent(args[0]))
            {
                if (rom == null) return;

                emulator.Load(rom);
            }
            
            Application.Run(new MainWindow(emulator));
        }

        private static Stream GetRomContent(string defaultPath)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = defaultPath;
                openFileDialog.Filter = "Chip8 ROM|*.ch8|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    return openFileDialog.OpenFile();
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
