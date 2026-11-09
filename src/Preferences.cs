using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace QingDu {
public sealed class Voice { public string id,name; public override string ToString(){return name;} }
public sealed class ProviderChoice { public string Id,Name; public override string ToString(){return Name;} }
public sealed class Preferences {
    public string Provider="mimo",FishModel="s2.1-pro-free",FishVoice="",MimoVoice="冰糖",MimoStyle="";
    public List<Voice> FishVoices=new List<Voice>();
    public int Volume=100,FishBufferMs=350,MimoBufferMs=350;
    public double Speed=1,Pitch=0;
    public bool Duck=false;
    public HotkeyBinding ReadHotkey=new HotkeyBinding(2,192),PauseHotkey=new HotkeyBinding(3,80),StopHotkey=new HotkeyBinding(3,83);
    public static Preferences Load(string directory){
        Preferences p;try{p=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(directory,"settings.json"),Encoding.UTF8))??new Preferences();}catch{p=new Preferences();}
        if(p.Provider!="fish"&&p.Provider!="mimo")p.Provider="mimo";
        if(p.FishModel!=FishClient.FreeModel&&p.FishModel!=FishClient.PaidModel)p.FishModel=FishClient.FreeModel;
        if(p.FishVoices==null)p.FishVoices=new List<Voice>();
        if(!MimoClient.ValidVoice(p.MimoVoice))p.MimoVoice="冰糖";
        p.MimoStyle=p.MimoStyle??"";p.Speed=Math.Max(.7,Math.Min(1.5,p.Speed));p.Pitch=Math.Max(-6,Math.Min(6,p.Pitch));p.Volume=Math.Max(0,Math.Min(100,p.Volume));
        p.FishBufferMs=Math.Max(100,Math.Min(2000,p.FishBufferMs));p.MimoBufferMs=Math.Max(100,Math.Min(2000,p.MimoBufferMs));
        if(!HotkeyManager.Valid(p.ReadHotkey))p.ReadHotkey=new HotkeyBinding(2,192);
        if(!HotkeyManager.Valid(p.PauseHotkey))p.PauseHotkey=new HotkeyBinding(3,80);
        if(!HotkeyManager.Valid(p.StopHotkey))p.StopHotkey=new HotkeyBinding(3,83);
        return p;
    }
    public void Save(string directory){
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"settings.json");
        File.WriteAllText(path+".tmp",new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
        if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
    }
}
}
