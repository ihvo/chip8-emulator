using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chip8.Host
{
    public partial class MainWindow : Form
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly Emulator emulator;

        private readonly Dictionary<int, Chip8.Key> keyMap = new Dictionary<int, Key>()
        {
            { 49, Key.K1 }, // 1
            { 50, Key.K2 }, // 2
            { 51, Key.K3 }, // 3
            { 52, Key.KC }, // 4
            { 81, Key.K4 }, // Q
            { 87, Key.K5 }, // W
            { 69, Key.K6 }, // E
            { 82, Key.KD }, // R
            { 65, Key.K7 }, // A
            { 83, Key.K8 }, // S
            { 68, Key.K9 }, // D
            { 70, Key.KE }, // F
            { 90, Key.KA }, // Z
            { 88, Key.K0 }, // X
            { 67, Key.KB }, // C
            { 86, Key.KF }, // V
        };

        public MainWindow(Emulator emulator)
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(Emulator.DisplayColumns, Emulator.DisplayRows);

            this.emulator = emulator;            
        }

        private void timer1_Tick(object sender, System.EventArgs e)
        {
            this.Invalidate();
        }

        private void MainWindow_Paint(object sender, PaintEventArgs e)
        {
            var displayBuffer = emulator.GetDisplayBuffer();

            var horizontalScale = (int)((double)this.Width / Emulator.DisplayColumns);
            var verticalScale = (int)((double)this.Height / Emulator.DisplayRows);

            e.Graphics.Clear(Color.Black);

            for (int pixelIndex = 0; pixelIndex < displayBuffer.Length; pixelIndex++)
            {
                if (displayBuffer[pixelIndex] > 0)
                {
                    var y = pixelIndex / Emulator.DisplayColumns;
                    var x = pixelIndex % Emulator.DisplayColumns;

                    e.Graphics.FillRectangle(Brushes.White, x * horizontalScale, y * verticalScale, horizontalScale, verticalScale);
                }
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (TryConvertToChip8Key(e.KeyValue, out var key))
            {
                emulator.KeyDown(key);

                e.Handled = true;
                e.SuppressKeyPress = true;
            }            
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (TryConvertToChip8Key(e.KeyValue, out var key))
            {
                emulator.KeyUp(key);

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private bool TryConvertToChip8Key(int keyValue, out Chip8.Key key)
        {
            if (keyMap.ContainsKey(keyValue))
            {
                key = keyMap[keyValue];
                return true;
            }

            key = Key.Undefined;
            return false;
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            Task.Run(() => emulator.Run(cancellationTokenSource.Token));
            this.renderingTimer.Start();
        }
    }
}
