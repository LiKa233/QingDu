using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace QingDu {
public sealed class AudioPlayer : IDisposable {
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort tag,channels;public uint rate,bytes;public ushort align,bits,extra;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr data;public uint length,recorded;public IntPtr user;public uint flags,loops;public IntPtr next,reserved;}
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr h,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr h,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr h);
    [DllImport("winmm.dll")] static extern uint waveOutPause(IntPtr h);
    [DllImport("winmm.dll")] static extern uint waveOutRestart(IntPtr h);
    sealed class Buffer {public byte[] bytes;public GCHandle pin;public IntPtr header;}
    readonly Queue<byte[]> queue=new Queue<byte[]>();readonly object gate=new object();readonly Thread thread;readonly double delay;readonly int minimumBytes;
    volatile bool stopped,finished,paused;byte[] current;int offset,queued;DateTime? first;IntPtr handle;
    public volatile float Volume=1f;public volatile bool IsPlaying;public Action Started,Completed;public Action<string> Failed;public int Underruns;public double MissingSeconds;
    public bool Paused {get{return paused;} set{paused=value;lock(gate){if(handle!=IntPtr.Zero){if(value)waveOutPause(handle);else waveOutRestart(handle);}}}}
    public AudioPlayer(double delaySeconds,double initialAudioSeconds=0){delay=delaySeconds;minimumBytes=(int)(initialAudioSeconds*48000);thread=new Thread(Run);thread.IsBackground=true;thread.Name="QingDu audio queue";thread.Start();}
    public void Enqueue(byte[] data,CancellationToken token){
        while(!stopped&&!token.IsCancellationRequested){lock(gate){if(queued<24000*2*20){queue.Enqueue(data);queued+=data.Length;if(!first.HasValue)first=DateTime.UtcNow;return;}}Thread.Sleep(20);}
    }
    public void Finish(){finished=true;}
    int Fill(byte[] buffer){int copied=0;lock(gate){while(copied<buffer.Length){if(current==null||offset>=current.Length){if(queue.Count==0)break;current=queue.Dequeue();offset=0;}int n=Math.Min(buffer.Length-copied,current.Length-offset);BufferBlock(current,offset,buffer,copied,n);offset+=n;copied+=n;queued-=n;}}return copied;}
    static void BufferBlock(byte[] a,int i,byte[] b,int j,int n){System.Buffer.BlockCopy(a,i,b,j,n);}
    void Free(Buffer b){if(handle!=IntPtr.Zero)waveOutUnprepareHeader(handle,b.header,(uint)Marshal.SizeOf(typeof(Header)));Marshal.FreeHGlobal(b.header);b.pin.Free();}
    void Run(){var active=new List<Buffer>();bool started=false;DateTime? missingStart=null;try{
        var format=new Format{tag=1,channels=1,rate=24000,bytes=48000,align=2,bits=16,extra=0};
        uint error=waveOutOpen(out handle,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,0);if(error!=0)throw new Exception("无法打开默认播放设备（代码 "+error+"）");
        while(!stopped){
            for(int i=active.Count-1;i>=0;i--){var h=(Header)Marshal.PtrToStructure(active[i].header,typeof(Header));if((h.flags&1)!=0){Free(active[i]);active.RemoveAt(i);}}
            if(paused){missingStart=null;Thread.Sleep(10);continue;}
            if(!started){if(!first.HasValue){if(finished)break;Thread.Sleep(5);continue;}int available;lock(gate){available=queued;}if(available<minimumBytes&&!finished){Thread.Sleep(5);continue;}if((DateTime.UtcNow-first.Value).TotalSeconds<delay){Thread.Sleep(5);continue;}}
            while(active.Count<3&&!stopped){
                byte[] bytes=new byte[1920];int n=Fill(bytes);if(n==0&&finished)break;
                if(n==0){if(active.Count==0&&!missingStart.HasValue)missingStart=DateTime.UtcNow;break;}
                if(missingStart.HasValue){double gap=(DateTime.UtcNow-missingStart.Value).TotalSeconds;if(gap>.02){Underruns++;MissingSeconds+=gap;}missingStart=null;}
                if(!started){started=true;IsPlaying=true;if(Started!=null)Started();}
                int length=n>0?n:bytes.Length;
                float volume=Volume;if(Math.Abs(volume-1)>.001){for(int j=0;j+1<length;j+=2){short sample=(short)(bytes[j]|bytes[j+1]<<8);int v=(int)(sample*volume);v=Math.Max(-32768,Math.Min(32767,v));bytes[j]=(byte)v;bytes[j+1]=(byte)(v>>8);}}
                var b=new Buffer{bytes=bytes};b.pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);b.header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));
                var h=new Header{data=b.pin.AddrOfPinnedObject(),length=(uint)length};Marshal.StructureToPtr(h,b.header,false);
                if(waveOutPrepareHeader(handle,b.header,(uint)Marshal.SizeOf(typeof(Header)))!=0){Marshal.FreeHGlobal(b.header);b.pin.Free();throw new Exception("音频缓冲初始化失败");}
                if(waveOutWrite(handle,b.header,(uint)Marshal.SizeOf(typeof(Header)))!=0){Free(b);throw new Exception("音频播放失败，请检查输出设备");}active.Add(b);
            }
            if(finished){lock(gate){if(queued==0&&active.Count==0)break;}}
            Thread.Sleep(5);
        }
    }catch(Exception e){if(!stopped&&Failed!=null)Failed(e.Message);}finally{
        lock(gate){if(handle!=IntPtr.Zero){waveOutReset(handle);foreach(var b in active)Free(b);waveOutClose(handle);handle=IntPtr.Zero;}}
        if(!stopped&&Completed!=null)Completed();
    }}
    public void Dispose(){stopped=true;if(Thread.CurrentThread!=thread)thread.Join(1500);}
}

[ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMEnumerator {}
[ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMEnumerator {
    [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr collection);
    [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
}
[ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice {
    [PreserveSig]int Activate(ref Guid iid,uint ctx,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)]out object result);
    [PreserveSig]int OpenPropertyStore(int access,out IntPtr props);
    [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
    [PreserveSig]int GetState(out uint state);
}
[ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IManager {
    [PreserveSig]int GetAudioSessionControl(IntPtr guid,uint flags,out IntPtr ctl);
    [PreserveSig]int GetSimpleAudioVolume(IntPtr guid,uint flags,out IntPtr vol);
    [PreserveSig]int GetSessionEnumerator(out ISessionEnumerator sessions);
}
[ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionEnumerator {
    [PreserveSig]int GetCount(out int count);
    [PreserveSig]int GetSession(int index,out ISession session);
}
[ComImport,Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISession {
    [PreserveSig]int GetState(out int state);
    [PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
    [PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr context);
    [PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);
    [PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,IntPtr context);
    [PreserveSig]int GetGroupingParam(out Guid grouping);
    [PreserveSig]int SetGroupingParam(ref Guid grouping,IntPtr context);
    [PreserveSig]int RegisterAudioSessionNotification(IntPtr events);
    [PreserveSig]int UnregisterAudioSessionNotification(IntPtr events);
    [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
    [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
    [PreserveSig]int GetProcessId(out uint pid);
    [PreserveSig]int IsSystemSoundsSession();
    [PreserveSig]int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)]bool optOut);
}
[ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISimpleVolume {
    [PreserveSig]int SetMasterVolume(float level,IntPtr context);
    [PreserveSig]int GetMasterVolume(out float level);
    [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,IntPtr context);
    [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
}
public sealed class AudioDucker:IDisposable {
    sealed class Saved{public ISimpleVolume volume;public float before,applied;public string id;}
    readonly Dictionary<string,Saved> saved=new Dictionary<string,Saved>();
    public readonly Dictionary<uint,float> Levels=new Dictionary<uint,float>();
    public int Probe(){return Apply(false);}
    public int Duck(){return Apply(true);}
    public int DuckOnly(uint pid){return Apply(true,pid);}
    int Apply(bool change,uint onlyPid=0){Levels.Clear();int seen=0;IMMEnumerator en=null;IMMDevice device=null;object manager=null;ISessionEnumerator sessions=null;
        try{en=(IMMEnumerator)new MMEnumerator();if(en.GetDefaultAudioEndpoint(0,0,out device)!=0)return 0;Guid iid=typeof(IManager).GUID;if(device.Activate(ref iid,23,IntPtr.Zero,out manager)!=0)return 0;((IManager)manager).GetSessionEnumerator(out sessions);int count;sessions.GetCount(out count);
            for(int i=0;i<count;i++){ISession session=null;try{sessions.GetSession(i,out session);uint pid;int state;string id;session.GetProcessId(out pid);session.GetState(out state);session.GetSessionInstanceIdentifier(out id);if(pid==0||pid==(uint)Process.GetCurrentProcess().Id||state!=1||(onlyPid!=0&&pid!=onlyPid))continue;seen++;float observed;((ISimpleVolume)session).GetMasterVolume(out observed);Levels[pid]=observed;if(!change||saved.ContainsKey(id))continue;var volume=(ISimpleVolume)session;float before;volume.GetMasterVolume(out before);float applied=before*.2f;if(volume.SetMasterVolume(applied,IntPtr.Zero)==0)saved[id]=new Saved{volume=volume,before=before,applied=applied,id=id};}
                catch{}finally{if(session!=null&&!Contains(session))Marshal.ReleaseComObject(session);}}
        }finally{if(sessions!=null)Marshal.ReleaseComObject(sessions);if(manager!=null)Marshal.ReleaseComObject(manager);if(device!=null)Marshal.ReleaseComObject(device);if(en!=null)Marshal.ReleaseComObject(en);}return seen;
    }
    bool Contains(object obj){foreach(var s in saved.Values)if(Object.ReferenceEquals(obj,s.volume))return true;return false;}
    public void Restore(){foreach(var s in saved.Values){try{float current;s.volume.GetMasterVolume(out current);if(Math.Abs(current-s.applied)<.005)s.volume.SetMasterVolume(s.before,IntPtr.Zero);}catch{}try{Marshal.ReleaseComObject(s.volume);}catch{}}saved.Clear();}
    public void Dispose(){Restore();}
}
}
