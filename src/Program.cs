using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly:System.Reflection.AssemblyTitle("轻读")]
[assembly:System.Reflection.AssemblyProduct("轻读")]
[assembly:System.Reflection.AssemblyCopyright("Copyright © QingDu contributors")]
namespace QingDu {
static class Program {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int msg,IntPtr a,IntPtr b);
    delegate bool EnumWindowProc(IntPtr h,IntPtr p);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindowProc callback,IntPtr p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
    [STAThread]static void Main(string[] args){
        bool created;using(var mutex=new Mutex(true,"Local\\QingDu.Reader.v01",out created)){
            if(!created){EnumWindows((h,p)=>{var title=new StringBuilder(256);GetWindowText(h,title,256);if(title.ToString().StartsWith("轻读 v",StringComparison.Ordinal)){PostMessage(h,args.Length>0&&args[0]=="--exit"?0x8002:0x8001,IntPtr.Zero,IntPtr.Zero);return false;}return true;},IntPtr.Zero);return;}
            if(args.Length>0&&args[0]=="--exit")return;
            ServicePointManager.DefaultConnectionLimit=8;ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"轻读",MessageBoxButtons.OK,MessageBoxIcon.Error);
            Application.Run(new ReaderForm());
        }
    }
}
}
