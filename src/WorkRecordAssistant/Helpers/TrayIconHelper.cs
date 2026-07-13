using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 系统托盘图标：隐藏到托盘、右键退出。
/// </summary>
public sealed class TrayIconHelper : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int TrayMessage = WM_USER + 1;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private readonly Window _window;
    private readonly Action _onExit;
    private readonly HwndSource _messageSource;
    private readonly Icon _icon;
    private readonly ContextMenu _contextMenu;
    private bool _isVisible;

    public TrayIconHelper(Window window, Action onExit)
    {
        _window = window;
        _onExit = onExit;
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? AppContext.BaseDirectory)
               ?? SystemIcons.Application;

        var parameters = new HwndSourceParameters("WorkRecordAssistantTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WndProc);

        _contextMenu = new ContextMenu();
        _contextMenu.Items.Add(new MenuItem
        {
            Header = "显示窗口",
            Command = new RelayCommand(ShowWindow)
        });
        _contextMenu.Items.Add(new Separator());
        _contextMenu.Items.Add(new MenuItem
        {
            Header = "退出",
            Command = new RelayCommand(_onExit)
        });
    }

    public void HideToTray()
    {
        _window.Hide();
        _window.ShowInTaskbar = false;
        UpdateTrayIcon(true);
    }

    public void ShowWindow()
    {
        _window.ShowInTaskbar = true;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
    }

    public void Dispose()
    {
        UpdateTrayIcon(false);
        _messageSource.RemoveHook(WndProc);
        _messageSource.Dispose();
        _icon.Dispose();
    }

    private void UpdateTrayIcon(bool visible)
    {
        var data = CreateData();
        if (visible && !_isVisible)
        {
            Shell_NotifyIcon(NIM_ADD, ref data);
            _isVisible = true;
        }
        else if (!visible && _isVisible)
        {
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _isVisible = false;
        }
        else if (visible)
        {
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _messageSource.Handle,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = TrayMessage,
        hIcon = _icon.Handle,
        szTip = "工作记录助手"
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != TrayMessage) return IntPtr.Zero;

        switch ((int)lParam)
        {
            case WM_LBUTTONDBLCLK:
                ShowWindow();
                handled = true;
                break;
            case WM_RBUTTONUP:
                _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _contextMenu.IsOpen = true;
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
