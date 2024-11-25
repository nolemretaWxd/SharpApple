using System.Drawing;
using System.Numerics;
using System.Reflection;
using Chroma;
using Chroma.ContentManagement;
using Chroma.ContentManagement.FileSystem;
using Chroma.Diagnostics.Logging;
using Chroma.Graphics;
using Chroma.Input;
using Chroma.Graphics.TextRendering.TrueType;
using Chroma.SabreVGA;
using NativeFileDialogSharp;
using Color = Chroma.Graphics.Color;

namespace SharpApple;
public class EmulatorMain : Game
{
    public static VgaScreen Screen;
    public static Color FgColor = Color.Lime;
    public static Log Log { get; } = LogManager.GetForCurrentAssembly();

    private RenderTarget _target;
    private TrueTypeFont _ttf;

    private Memory _mem;
    private MOS6502 _cpu;

    private ushort _ramSize = 16384;
    private ushort _width = 1024;
    private ushort _height = 768;
    private bool _debug;

    private bool _isInMenu;
    private byte _menuSelection;
    private SubMenus _submenu;
    private bool _readingInput;
    private string _inputBuffer;
    private ushort _startAddress;

    private static Version? _version = Assembly.GetExecutingAssembly().GetName().Version;
    private static DateTime _buildDate = new DateTime(2000, 1, 1)
        .AddDays(_version.Build).AddSeconds(_version.Revision * 2);

    #region Important stuff

    public EmulatorMain()
        : base(new(false, false))
    {
        // parse commandline
        string[] commandline = Environment.CommandLine.Split(" ");
        for (int i = 1; i <= commandline.Length - 1; i++)
        {
            if (commandline[i] == "--ram" || commandline[i] == "-r")
                _ramSize = ushort.Parse(commandline[i + 1]);
            if (commandline[i] == "--height" || commandline[i] == "-h")
                _height = ushort.Parse(commandline[i + 1]);
            if (commandline[i] == "--width" || commandline[i] == "-w")
                _width = ushort.Parse(commandline[i + 1]);
            if (commandline[i] == "--debug")
                _debug = true;
        }

        Window.Title = "SharpApple";
        Window.Mode.SetWindowed(_width, _height);
        _target = new RenderTarget(360, 232);
        _target.FilteringMode = TextureFilteringMode.NearestNeighbor;
        _target.VirtualResolution = new Size(Window.Width, Window.Height);
        FixedTimeStepTarget = 60; // set internal clock to 60 hz, this way screen will refresh every clock cycle,
                                  // and 6502 will run 17050 (1 MHz / 60 Hz) cycles every frame
    }

    protected override void Initialize(IContentProvider content)
    {
        _ttf = content.Load<TrueTypeFont>("Px437_IBM_CGA.ttf", 8);
        Screen = new VgaScreen(new Vector2(20, 20), new Size(320, 192), _ttf, 8, 8);
        Screen.Cursor.Padding = new Size(0, 1);
        Screen.Cursor.Shape = CursorShape.Underscore;
        Screen.Cursor.Blink = true;
        Screen.Cursor.IsVisible = true;
        Screen.Cursor.Color = FgColor;
        Screen.ClearToColor(FgColor, Color.Black);

        // initialize emulated machine
        _mem = new Memory(_ramSize, File.ReadAllBytes("apple1.rom"), File.ReadAllBytes("basic.rom"));
        _cpu = new MOS6502(_mem);

        Write($"SharpApple - Apple 1 emulator\n" +
              $"Press 'Tab' to access menu\n" +
              $"{_mem.RamSize/1024}K RAM\n");

        if (_debug)
        {
            Write("Running in debug mode\n");
        }

        //Log.Debug($"Stack pointer is at {_cpu.state.s.ToString("X")}");
        _cpu.Reset();
    }

    protected override IContentProvider InitializeContentPipeline()
    {
        return new FileSystemContentProvider(AppContext.BaseDirectory);
    }

    protected override void TextInput(TextInputEventArgs e)
    {
        if (!_isInMenu)
        {
            foreach (char chr in e.Text)
            {
                // blacklisted characters
                if (chr == '_')
                    continue;
                _mem.Kbd = Char.ToUpper(chr);
            }
        }
        else
        {
            if (_readingInput)
            {
                _inputBuffer += e.Text.ToUpper();
            }
        }
    }

    protected override void Update(float delta)
    {
        Screen.Update(delta);
        Screen.Cursor.Blink = true;
        Screen.Cursor.IsVisible = true;
        Screen.Cursor.Color = FgColor;
    }

    protected override void Draw(RenderContext context)
    {
        context.RenderTo(_target, (ctx, _) =>
        {
            context.Clear(Color.Black);

            Screen.Draw(context);

            if (_debug)
            {
                context.DrawString(_ttf, $" (PC: {_cpu.PC.ToString("X2")}, opcode: {_cpu.Opcode.ToString("X2")})", 0, 0, Color.Lime);
            }

            if (_isInMenu)
            {
                context.Rectangle(ShapeMode.Fill, 0, 0, 360, 232, new Color(0, 0, 0, 1));
                switch (_submenu)
                {
                    case SubMenus.None:
                        context.DrawString(_ttf, $"Menu", 30, 30, Color.Lime);
                        context.DrawString(_ttf, _menuSelection == 0 ? "> Load into RAM" : "Load into RAM", 30, 46, Color.Lime);
                        context.DrawString(_ttf, _menuSelection == 1 ? "> Save from RAM" : "Save from RAM", 30, 54, Color.Lime);
                        context.DrawString(_ttf, _menuSelection == 2 ? "> About" : "About", 30, 62, Color.Lime);
                        break;
                    case SubMenus.Addr:
                        context.DrawString(_ttf, "Address to load into:", 30, 30, Color.Lime);
                        context.DrawString(_ttf, _inputBuffer, 30, 38, Color.Lime);
                        break;
                    case SubMenus.AddrStart:
                        context.DrawString(_ttf, "Starting address:", 30, 30, Color.Lime);
                        context.DrawString(_ttf, _inputBuffer, 30, 38, Color.Lime);
                        break;
                    case SubMenus.AddrEnd:
                        context.DrawString(_ttf, "Ending address:", 30, 30, Color.Lime);
                        context.DrawString(_ttf, _inputBuffer, 30, 38, Color.Lime);
                        break;
                    case SubMenus.About:
                        context.DrawString(_ttf, "SharpApple - Apple 1 emulator\n\n" +
                                                 $"Version {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}\n"+
                                                 "Written in C#\n" +
                                                 "and using Chroma Framework\n" +
                                                 "(c) 2024 krnlException\n\n" +
                                                 "Thanks to:\n" +
                                                 "  vddCore - For Chroma Framework\n" +
                                                 "  Omnicrash - For 6502 emulator", 30, 30, Color.Lime);
                        break;
                }
            }
        });
        context.DrawTexture(
            _target,
            Vector2.Zero,
            Vector2.One,
            Vector2.Zero,
            0
        );
    }

    protected override void KeyPressed(KeyEventArgs e)
    {
        if (!_isInMenu)
        {
            switch (e.KeyCode)
            {
                case KeyCode.Return:
                    _mem.Kbd = (char)0x8D;
                    break;
                case KeyCode.Backspace:
                    _mem.Kbd = '_';
                    break;
                case KeyCode.Escape:
                    _cpu.Reset();
                    break;
            }
        }
        else
        {
            if (_submenu == SubMenus.None)
            {
                switch (e.KeyCode)
                {
                    case KeyCode.Down:
                        if (_menuSelection < 2)
                            _menuSelection++;
                        break;
                    case KeyCode.Up:
                        if (_menuSelection > 0)
                            _menuSelection--;
                        break;
                    case KeyCode.Return:
                        if (_menuSelection == 0)
                        {
                            _submenu = SubMenus.Addr;
                            _readingInput = true;
                        }
                        else if (_menuSelection == 1)
                        {
                            _submenu = SubMenus.AddrStart;
                            _readingInput = true;
                        }
                        else if (_menuSelection == 2)
                            _submenu = SubMenus.About;
                        _inputBuffer = "";
                        break;
                }
            }
            else
            {
                switch (e.KeyCode)
                {
                    case KeyCode.Backspace:
                        if (_readingInput && _inputBuffer != "")
                        {
                            _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                        }
                        break;
                    case KeyCode.Return:
                        HandleMenus();
                        break;
                    case KeyCode.Escape:
                        _submenu = SubMenus.None;
                        _readingInput = false;
                        _inputBuffer = "";
                        _startAddress = 0;
                        break;
                }
            }
        }

        if (e.KeyCode == KeyCode.Tab && _submenu == SubMenus.None)
            _isInMenu = !_isInMenu;
    }

    protected override void FixedUpdate(float delta)
    {
        if (!_isInMenu)
        {
            for (int i = 0; i < 17050; i++)
            {
                _cpu.Process();
            }
        }
    }

    #endregion

    #region I/O methods

    public static void NextLine()
    {
        Screen.Cursor.X = 0;
        if (Screen.Cursor.Y < Screen.WindowRows)
        {
            Screen.Cursor.Y++;
        }
        else
        {
            Screen.Scroll();
        }
    }

    public void Write(string text)
    {
        foreach (char character in text)
        {
            if (character == '\n')
            {
                NextLine();
            }
            else
            {
                Screen.PutCharAt(Screen.Cursor.X++, Screen.Cursor.Y, character, FgColor, Color.Black, false);
            }
        }
    }

    public static void Backspace()
    {
        int cursorX = Screen.Cursor.X;
        if (cursorX == 1) return;
        Screen[cursorX - 1, Screen.Cursor.Y].Character = '\0';
        Screen.Cursor.X--;
    }

    #endregion

    #region Menu handling

    private void HandleMenus()
    {
        if (_submenu == SubMenus.Addr)
        {
            _readingInput = false;
            ushort address = Convert.ToUInt16(_inputBuffer, 16);
            DialogResult file = Dialog.FileOpen();
            byte[] binFile = File.ReadAllBytes(file.Path);
            Array.Copy(binFile, 0, _mem.Ram, address, binFile.Length);
            _submenu = SubMenus.None;
        } else if (_submenu == SubMenus.AddrStart)
        {
            _startAddress = Convert.ToUInt16(_inputBuffer, 16);
            _submenu = SubMenus.AddrEnd;
        } else if (_submenu == SubMenus.AddrEnd)
        {
            DialogResult file = Dialog.FileSave();
            _readingInput = false;
            ushort addrEnd = Convert.ToUInt16(_inputBuffer, 16);
            byte[] region = new byte[addrEnd - _startAddress + 1];
            Array.Copy(_mem.Ram, _startAddress, region, 0, addrEnd - _startAddress + 1);
            File.WriteAllBytes(file.Path, region);
            _submenu = SubMenus.None;
            _startAddress = 0;
        } else if (_submenu == SubMenus.About)
        {
            _submenu = SubMenus.None;
        }

        _inputBuffer = "";
    }

    #endregion
}

public enum SubMenus
{
    None,
    Addr,
    AddrStart,
    AddrEnd,
    About
}