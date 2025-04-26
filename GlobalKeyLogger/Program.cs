using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class GlobalKeyLogger : ApplicationContext
{
    private static Stopwatch stopwatch = Stopwatch.StartNew();
    private static StreamWriter writer;
    private static long lastKeyDownTime = 0;
    private static bool firstKey = true;
    private static long flightTime = -1;
    private static IntPtr _hookID = IntPtr.Zero;
    private static Dictionary<int, long> keyDownTimes = new Dictionary<int, long>();
    private static NotifyIcon trayIcon;
    private static bool isRecording = true;
    private static Icon recordIcon;
    private static Icon pauseIcon;
    private static string logFileName;

    private static LowLevelKeyboardProc _proc = HookCallback;

    public GlobalKeyLogger()
    {
        // Load icons
        recordIcon = CreateRedDotIcon();
        pauseIcon = CreatePauseIcon();

        // Create tray icon
        trayIcon = new NotifyIcon()
        {
            Icon = recordIcon,
            Visible = true,
            Text = "Global Keylogger"
        };

        // Create context menu
        trayIcon.ContextMenuStrip = new ContextMenuStrip();
        trayIcon.ContextMenuStrip.Items.Add("Pause", null, PauseLogging);
        trayIcon.ContextMenuStrip.Items.Add("Exit", null, Exit);

        trayIcon.DoubleClick += TrayIcon_DoubleClick;

        // Setup keylogger
        CreateNewLogFile();
        _hookID = SetHook(_proc);
    }

    private void CreateNewLogFile()
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        logFileName = $"keylog_{timestamp}.csv";

        writer = new StreamWriter(logFileName, false); // overwrite mode (false)
        writer.WriteLine("VK,HT,FT");
        writer.Flush();
    }

    private void TrayIcon_DoubleClick(object sender, EventArgs e)
    {
        MessageBox.Show($"Keylogger is {(isRecording ? "recording" : "paused")}.\nLogging to:\n{logFileName}\n\nRight-click tray icon for options.", "Global Keylogger");
    }

    private void PauseLogging(object sender, EventArgs e)
    {
        isRecording = !isRecording;
        trayIcon.Icon = isRecording ? recordIcon : pauseIcon;
        trayIcon.ContextMenuStrip.Items[0].Text = isRecording ? "Pause" : "Resume";
    }

    private void Exit(object sender, EventArgs e)
    {
        trayIcon.Visible = false;
        writer?.Close();
        UnhookWindowsHookEx(_hookID);
        Application.Exit();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(
        int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(
        int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (isRecording)
            {
                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    long currDownTime = stopwatch.ElapsedMilliseconds;

                    if (firstKey)
                    {
                        flightTime = -1;
                        firstKey = false;
                    }
                    else
                    {
                        flightTime = currDownTime - lastKeyDownTime;
                        if (flightTime > 1500)
                            flightTime = -1;
                    }

                    lastKeyDownTime = currDownTime;
                    keyDownTimes[vkCode] = currDownTime;
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    if (keyDownTimes.TryGetValue(vkCode, out long downTime))
                    {
                        long releaseTime = stopwatch.ElapsedMilliseconds;
                        long holdTime = releaseTime - downTime;

                        writer.WriteLine($"{vkCode},{holdTime},{flightTime}");
                        writer.Flush();

                        keyDownTimes.Remove(vkCode);
                    }
                }
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static Icon CreateRedDotIcon()
    {
        Bitmap bmp = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.Red, 2, 2, 12, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static Icon CreatePauseIcon()
    {
        Bitmap bmp = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.RoyalBlue, 4, 2, 3, 12);
            g.FillRectangle(Brushes.RoyalBlue, 9, 2, 3, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new GlobalKeyLogger());
    }
}
