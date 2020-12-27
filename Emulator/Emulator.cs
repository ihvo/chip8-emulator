using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Chip8
{
    public class Emulator
    {
        /* The first CHIP-8 interpreter (on the COSMAC VIP computer) was also located in RAM, from address 000 to 1FF. 
           It would expect a CHIP-8 program to be loaded into memory after it, starting at address 200 (512 in decimal).*/
        private const int romOffset = 0x200;
        private const short stackLength = 0x20;
        private const int FontOffset = 0x50;

        public const byte DisplayRows = 32;
        public const byte DisplayColumns = 64;

        readonly byte[] font = new byte[]
        {
            0xF0, 0x90, 0x90, 0x90, 0xF0, // 0
            0x20, 0x60, 0x20, 0x20, 0x70, // 1
            0xF0, 0x10, 0xF0, 0x80, 0xF0, // 2
            0xF0, 0x10, 0xF0, 0x10, 0xF0, // 3
            0x90, 0x90, 0xF0, 0x10, 0x10, // 4
            0xF0, 0x80, 0xF0, 0x10, 0xF0, // 5
            0xF0, 0x80, 0xF0, 0x90, 0xF0, // 6
            0xF0, 0x10, 0x20, 0x40, 0x40, // 7
            0xF0, 0x90, 0xF0, 0x90, 0xF0, // 8
            0xF0, 0x90, 0xF0, 0x10, 0xF0, // 9
            0xF0, 0x90, 0xF0, 0x90, 0x90, // A
            0xE0, 0x90, 0xE0, 0x90, 0xE0, // B
            0xF0, 0x80, 0x80, 0x80, 0xF0, // C
            0xE0, 0x90, 0x90, 0x90, 0xE0, // D
            0xF0, 0x80, 0xF0, 0x80, 0xF0, // E
            0xF0, 0x80, 0xF0, 0x80, 0x80, // F
        };

        private byte[] memory;
        private byte[] displayMemory;
        private Stack<ushort> stack = new Stack<ushort>(stackLength);

        private ushort rPC;
        private ushort renderIndex;
        private byte[] rVar;

        private byte delayTimer;
        private byte soundTimer;

        private byte[] keysPressed;

        private bool paused;

        private Random random = new Random(Environment.TickCount);

        public ReadOnlySpan<byte> GetDisplayBuffer()
        {
            return new ReadOnlySpan<byte>(displayMemory);
        }

        public void Load(Stream rom)
        {
            int destinationOffset = romOffset;

            const int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];

            int bytesRead;
            while ((destinationOffset < memory.Length) 
                && (bytesRead = rom.Read(buffer, 0, Math.Min(buffer.Length, memory.Length - destinationOffset))) > 0)
            {
                Array.Copy(buffer, 0, memory, destinationOffset, bytesRead);
                destinationOffset += bytesRead;
            }
        }

        public void Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var start = Stopwatch.GetTimestamp();

                var opCode = Fetch();
                Execute(opCode);

                if (!this.paused)
                {
                    UpdateTimers();
                }

                // roughly 700 ops per second
                while ((Stopwatch.GetTimestamp() - start) < Stopwatch.Frequency / 700)
                {
                    Thread.Yield();
                }
            }
        }

        private void UpdateTimers()
        {
            if (this.delayTimer > 0)
            {
                this.delayTimer--;
            }

            if (this.soundTimer > 0)
            {
                this.soundTimer--;
            }
        }

        public void KeyDown(Key key)
        {
            this.keysPressed[(int)key] = 1;
        }

        public void KeyUp(Key key)
        {
            this.keysPressed[(int)key] = 0;
        }

        private bool KeyPressed(Key key)
        {
            return this.keysPressed[(int)key] == 1;
        }

        private bool TryGetKeyDown(out byte key)
        {
            for (key = 0; key < this.keysPressed.Length; key++)
            {
                if (this.keysPressed[key] == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private ushort Fetch()
        {
            return (ushort)((memory[this.rPC++] << 8) | memory[this.rPC++]);
        }

        private void Execute(ushort opCode)
        {
            byte x = (byte)((opCode >> 8) & 0x0F);

            byte y = (byte)((opCode >> 4) & 0x0F);

            switch (opCode & 0xF000)
            {
                case 0x0000:
                    switch (opCode)
                    {
                        // 00E0 clear display
                        case 0x00E0:
                            ClearDisplay();
                            break;
                        // 00EE call return
                        case 0x00EE:
                            ReturnCall();
                            break;
                    }
                    break;
                
                // 1NNN jump
                case 0x1000:
                    Jump(ThreeNibbles(opCode));
                    break;

                // 2NNN Call
                case 0x2000:
                    Call(ThreeNibbles(opCode));
                    break;
                
                // 3XNN will skip one instruction if the value in VX is equal to NN
                case 0x3000:
                    SkipIf(GetVr(x) == Lsb(opCode));
                    break;

                // 4XNN will skip if the value in VX is not equal to NN
                case 0x4000:
                    SkipIf(GetVr(x) != Lsb(opCode));
                    break;

                // 5XY0 skips if the values in VX and VY are equal
                case 0x5000:
                    SkipIf(GetVr(x) == GetVr(y));
                    break;

                // 6XNN(set register VX)
                case 0x6000:
                    SetVr(x, Lsb(opCode));
                    break;

                // 7XNN(add value to register VX)
                case 0x7000:
                    AddConst(x, Lsb(opCode));
                    break;
                
                // Logical and arithmetic instructions
                case 0x8000:
                    switch (RNibble(Lsb(opCode)))
                    {
                        // 8XY0: Set
                        case 0:
                            SetVr(x, GetVr(y));
                            break;

                        // 8XY1: Bitwise OR
                        case 1:
                            SetVr(x, (byte)(GetVr(x) | GetVr(y)));
                            break;

                        // 8XY2: Bitwise AND
                        case 2:
                            SetVr(x, (byte)(GetVr(x) & GetVr(y)));
                            break;

                        // 8XY3: Bitwise XOR
                        case 3:
                            SetVr(x, (byte)(GetVr(x) ^ GetVr(y)));
                            break;

                        // 8XY4: Addition
                        case 4:
                            AddVr(x, y);
                            break;

                        // 8XY5 sets VX to the result of VX - VY
                        case 5:
                            SubtractVr(x, y);
                            break;
                        
                        // 8XY6 Shifts Vx right
                        case 6:
                            ShiftRightVr(x);
                            break;
                        
                        // 8XY7 sets VX to the result of VY -VX
                        case 7:
                            SubtractVrReversed(x, y);
                            break;

                        // 8XYE Shifts Vx left
                        case 0xE:
                            ShiftLeftVr(x);
                            break;
                    }
                    break;

                // 9XY0: skips if the values in VX and VY are not equal
                case 0x9000:
                    SkipIf(GetVr(x) != GetVr(y));
                    break;

                // ANNN: set index register I
                case 0xA000:
                    SetIr(ThreeNibbles(opCode));
                    break;

                // BNNN: Jump with offset V0
                case 0xB000:
                    Jump((ushort)(ThreeNibbles(opCode) + GetVr(0)));
                    break;

                // CXNN: Random
                case 0xC000:
                    SetVr(x, (byte)(GetRandom() & Lsb(opCode)));
                    break;

                // DXYN: display/draw
                case 0xD000:
                    RenderSprite(x, y, RNibble(Lsb(opCode)));
                    break;

                // EX9E: skip one instruction if the key corresponding to the value in VX is pressed/not pressed
                case 0xE000:
                    switch (Lsb(opCode))
                    {
                        // skip if the key is pressed
                        case 0x9E:
                            SkipIf(KeyPressed((Key)GetVr(x)));
                            break;

                        // skip if key is not pressed
                        case 0xA1:
                            SkipIf(!KeyPressed((Key)GetVr(x)));
                            break;
                    }
                    break;
                    
                case 0xF000:
                    switch (Lsb(opCode))
                    {
                        // FX07 sets VX to the current value of the delay timer
                        case 0x07:
                            SetVr(x, this.delayTimer);
                            break;

                        // FX0A: Get key
                        case 0x0A:
                            ContinueWhenKeyDown(x);
                            break;

                        // FX15 sets the delay timer to the value in VX
                        case 0x15:
                            delayTimer = x;
                            break;

                        // FX18 sets the sound timer to the value in VX
                        case 0x18:
                            soundTimer = x;
                            break;

                        // FX1E: Add to index
                        case 0x1E:
                            this.SetIr((ushort)(this.GetIr() + GetVr(x)));
                            break;

                        // FX29: Font character
                        case 0x29:
                            this.SetIr((ushort)(FontOffset + GetVr(x) * 5)); // each sprite is 5 bytes long
                            break;

                        // FX33: Binary-coded decimal conversion
                        case 0x33:
                            StoreDecimalDigits(x);
                            break;

                        // FX55: Store registers
                        case 0x55:
                            StoreVr(x);
                            break;

                        // FX65: Load registers
                        case 0x65:
                            LoadVr(x);
                            break;
                    }
                    break;
                default:
                    throw new NotImplementedException("OpCode not supported.");
            }
        }

        private void LoadVr(byte n)
        {
            for (int r = 0; r <= n; r++)
            {
                this.rVar[r] = this.memory[this.renderIndex + r];
            }
        }

        private void StoreVr(byte n)
        {
            for (int r = 0; r <= n; r++)
            {
                this.memory[this.renderIndex + r] = this.rVar[r];
            }
        }

        private void StoreDecimalDigits(byte n)
        {
            this.memory[this.renderIndex] = (byte)(n / 100);
            this.memory[this.renderIndex + 1] = (byte)(n % 100 / 10);
            this.memory[this.renderIndex + 2] = (byte)(n % 10);
        }

        private void ContinueWhenKeyDown(byte r)
        {
            if (this.TryGetKeyDown(out var key))
            {
                SetVr(r, key);
                this.paused = false;
            }
            else
            {
                this.paused = true;
                this.rPC -= 2;
            }
        }


        private ushort GetIr()
        {
            return this.renderIndex;
        }

        private byte GetRandom()
        {
            return (byte)this.random.Next(0, 256);
        }

        private void ShiftLeftVr(byte r)
        {
            this.rVar[0xF] = (byte)(this.rVar[r] & 0x80);
            this.rVar[r] <<= 1;
        }

        private void ShiftRightVr(byte r)
        {
            this.rVar[0xF] = (byte)(this.rVar[r] & 1);
            this.rVar[r] >>= 1;
        }

        private void SubtractVr(byte r1, byte r2)
        {
            SetVr(0xF, GetVr(r1) > GetVr(r2) ? 1 : 0);
            this.rVar[r1] -= this.rVar[r2];
        }

        private void SubtractVrReversed(byte r1, byte r2)
        {
            SetVr(0xF, GetVr(r2) > GetVr(r1) ? 1 : 0);
            this.rVar[r1] = (byte)(this.rVar[r2] - this.rVar[r1]);
        }

        private void AddVr(byte r1, byte r2)
        {
            var sum = GetVr(r1) + GetVr(r2);
            this.rVar[r1] = (byte)sum;
            SetVr(0xF, sum > 0xFF ? 1 : 0);
        }

        private void SkipIf(bool shouldSkip)
        {
            if (shouldSkip)
            {
                this.rPC += 2;
            }
        }

        private void ReturnCall()
        {
            this.rPC = this.stack.Pop();
        }

        private void Call(ushort address)
        {
            this.stack.Push(this.rPC);
            this.rPC = address;
        }

        private void RenderSprite(byte vx, byte vy, byte n)
        {
            var x = this.rVar[vx] % DisplayColumns;
            var y = this.rVar[vy] % DisplayRows;

            this.rVar[0xF] = 0;

            for (var spriteRow = 0; spriteRow < n; spriteRow++)
            {
                if (y + spriteRow >= DisplayRows)
                {
                    // don't wrap around sprites at the bottom edge of the screen
                    break;
                }

                var sprite = this.memory[this.renderIndex + spriteRow];

                for (var spriteColumn = 0; spriteColumn < 8; spriteColumn++)
                {
                    if (x + spriteColumn >= DisplayColumns)
                    {
                        // don't wrap around sprites at the right edge of the screen
                        break;
                    }

                    var pixelIndex = ((y + spriteRow) * DisplayColumns) + x + spriteColumn;

                    if ((sprite & (0x80 >> spriteColumn)) != 0)
                    {
                        if (displayMemory[pixelIndex] != 0)
                        {
                            // collision detected
                            this.rVar[0xF] = 1;
                        }

                        displayMemory[pixelIndex] ^= 1;
                    }                    
                }
            }
        }

        private void SetIr(ushort v)
        {
            this.renderIndex = v;
        }

        private void AddConst(byte r, byte val)
        {
            this.rVar[r] += val;
        }

        private void SetVr(byte r, byte val)
        {
            this.rVar[r] = val;
        }

        private byte GetVr(byte r)
        {
            return this.rVar[r];
        }

        private byte RNibble(byte b)
        {
            return (byte)(b & 0xF);
        }

        private byte Lsb(ushort b)
        {
            return (byte)(b & 0xFF);
        }

        private ushort ThreeNibbles(ushort w)
        {
            return (ushort)(w & 0xFFF);
        }

        private void Jump(ushort location)
        {
            this.rPC = location;
        }

        private void ClearDisplay()
        {
            this.displayMemory = new byte[DisplayRows * DisplayColumns];
        }

        public void Reset()
        {
            this.rPC = romOffset;
            this.renderIndex = 0;
            this.rVar =  new byte[16];
            this.stack.Clear();
            this.keysPressed = new byte[Enum.GetNames(typeof(Key)).Length];
            this.paused = false;

            ClearDisplay();
            this.memory = new byte[4096];

            // Putting font sprites at the start of memory block
            for (int i = 0; i < font.Length; i++)
            {
                memory[i + FontOffset] = font[i];
            }
        }
    }
}