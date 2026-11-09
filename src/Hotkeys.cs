using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QingDu {
public enum ReaderAction { Read,Pause,Stop }
public sealed class HotkeyBinding {
    public int Modifiers,Key;
    public HotkeyBinding(){} public HotkeyBinding(int modifiers,int key){Modifiers=modifiers;Key=key;}
    public override string ToString(){return ((Modifiers&2)!=0?"Ctrl+":"")+((Modifiers&1)!=0?"Alt+":"")+((Modifiers&4)!=0?"Shift+":"")+(Key==192?"`":((Keys)Key).ToString());}
    public bool Same(HotkeyBinding other){return other!=null&&Modifiers==other.Modifiers&&Key==other.Key;}
}
public interface IHotkeyBackend {bool Register(int id,HotkeyBinding binding);void Unregister(int id);}
public sealed class WindowsHotkeyBackend:IHotkeyBackend {
    readonly IntPtr handle;
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint vk);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
    public WindowsHotkeyBackend(IntPtr h){handle=h;}
    public bool Register(int id,HotkeyBinding b){return RegisterHotKey(handle,id,(uint)(b.Modifiers|0x4000),(uint)b.Key);}
    public void Unregister(int id){UnregisterHotKey(handle,id);}
}
public sealed class HotkeyManager:IDisposable {
    readonly IHotkeyBackend backend;readonly Dictionary<ReaderAction,int> ids=new Dictionary<ReaderAction,int>();readonly Dictionary<ReaderAction,HotkeyBinding> bindings=new Dictionary<ReaderAction,HotkeyBinding>();int nextId=100;
    public HotkeyManager(IHotkeyBackend backend){this.backend=backend;}
    public static bool Valid(HotkeyBinding b){return b!=null&&(b.Modifiers&~7)==0&&(b.Modifiers&3)!=0&&b.Key>0&&b.Key<256&&b.Key!=16&&b.Key!=17&&b.Key!=18&&b.Key!=91&&b.Key!=92&&(b.Key<160||b.Key>165);}
    public bool TrySet(ReaderAction action,HotkeyBinding binding,out string error){
        if(!Valid(binding)){error="请使用包含 Ctrl 或 Alt 的组合键。";return false;}
        foreach(var entry in bindings)if(entry.Key!=action&&entry.Value.Same(binding)){error="已用于“"+Name(entry.Key)+"”，请选择不同的组合键。";return false;}
        HotkeyBinding old;if(bindings.TryGetValue(action,out old)&&old.Same(binding)){error=null;return true;}
        int id=nextId++;if(!backend.Register(id,binding)){error="“"+Name(action)+"”快捷键 "+binding+" 已被系统或其他程序占用，原设置保持不变。";return false;}
        int previous;if(ids.TryGetValue(action,out previous))backend.Unregister(previous);
        ids[action]=id;bindings[action]=new HotkeyBinding(binding.Modifiers,binding.Key);error=null;return true;
    }
    public bool Resolve(int id,out ReaderAction action){foreach(var entry in ids)if(entry.Value==id){action=entry.Key;return true;}action=ReaderAction.Read;return false;}
    public HotkeyBinding Binding(ReaderAction action){HotkeyBinding b;return bindings.TryGetValue(action,out b)?b:null;}
    public static string Name(ReaderAction action){return action==ReaderAction.Read?"朗读":action==ReaderAction.Pause?"暂停／继续":"停止";}
    public void Dispose(){foreach(var id in ids.Values)backend.Unregister(id);ids.Clear();bindings.Clear();}
}
}
