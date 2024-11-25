using Chroma.Graphics;
using Chroma.SabreVGA;

namespace SharpApple;

public class Memory
{
    public byte[] Ram { get; set; }// default memory size is 16 kB

    public ushort RamSize => (ushort)Ram.Length;

    public byte[] Rom { get; }
    public byte[] Basic { get; }

    public ushort Reset { get; } = 0xFF00;

    public char Kbd = '\0'; // keyboard register

    private long _nextDsp = 0;

    public const ushort DspReg = 0xD012;
    public const ushort DspCrReg = 0xD013;
    public const ushort KbdReg = 0xD010;
    public const ushort KbdCrReg = 0xD011;


    public Memory(ushort size, byte[] rom, byte[] basic)
    {
        if (size > 0xD00F)
            throw new Exception($"Requested RAM exceeds max RAM allowed: {size} > 53,263");
        Ram = new byte[size];
        Rom = rom;
        Basic = basic;
    }

    public byte this[int address]
    {
        get
        {
            if (address >= 0 && address <= Ram.Length - 1) // RAM area
            {
                return Ram[address];
            }
            else if (address >= 0xD10 && address <= 0xD013) // PIA area
            {
                switch (address)
                {
                    case KbdReg: // computer is reading from the keyboard
                        if (Kbd != '\0')
                        {
                            char kbd = Kbd;
                            Kbd = '\0';
                            return (byte)(kbd | 0x80);
                        }
                        return 0x00;
                    case KbdCrReg: // return 0x80 if keyboard is something
                        return (byte)(Kbd != '\0' ? 0x80 : 0x00);
                    case DspReg: // what the fuck, apparently makes this work
                        return (byte)(DateTime.Now.ToFileTimeUtc() > _nextDsp ? 0x00 : 0x80);
                }
            }
            else if (address >= 0xE000 && address <= 0xEFFF)
            {
                return Basic[address - 0xE000];
            }
            else if (address >= 0xFF00 && address <= 0xFFFB) // ROM area
            {
                return Rom[address - 0xFF00];
            }
            else if (address == 0xFFFC)
                return (byte)Reset;
            else if (address == 0xFFFD)
                return (byte)(Reset >> 8);


            return 0;
        }
        set
        {
            // in apple 1 memory map, cpu can write to only 2 locations
            // RAM and PIA I/O
            // Otherwise, throw an IllegalWrite exception
            if (address >= 0 && address <= Ram.Length - 1) // RAM
            {
                Ram[address] = value;
            }
            else if (address >= 0xD10 && address <= 0xD013) // PIA
            {
                switch (address)
                {
                    case DspReg: // computer is outputting something to the screen - do some stuff with character
                        if ((value & 0b01111111) == 13)
                            EmulatorMain.NextLine();
                        if ((value & 0b01111111) == '_')
                            EmulatorMain.Backspace();
                        else
                        {
                            if ((value & 0b01111111) >= 32 && (value & 0b01111111) <= 95)
                                EmulatorMain.Screen.PutCharAt(EmulatorMain.Screen.Cursor.X++, EmulatorMain.Screen.Cursor.Y, (char)(value & 0b01111111), EmulatorMain.FgColor, Color.Black, false);

                            if (EmulatorMain.Screen.Cursor.X >= EmulatorMain.Screen.WindowColumns)
                            {
                                EmulatorMain.NextLine();
                            }
                        }
                        // some weird shit with registers wtf
                        _nextDsp = DateTime.Now.ToFileTimeUtc() + (Kbd != 0 ? 0 : 17);
                        break;
                }
            }
            else
            {
                EmulatorMain.Log.Error($"Cannot write at {address}");
            }
        }
    }

    public virtual byte Read(ushort address)
    {
        return this[address];
    }

    public virtual void Write(ushort address, byte value)
    {
        this[address] = value;
    }

    public ushort Read16(ushort address)
    {
        byte a = Read(address);
        byte b = Read(++address);
        return (ushort)((b << 8) | a);
    }
}