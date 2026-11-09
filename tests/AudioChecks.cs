using System;
using System.Diagnostics;
using System.Threading;
using QingDu;
class AudioChecks {
 static void Assert(bool b,string message){if(!b)throw new Exception(message);Console.WriteLine("PASS "+message);}
 public static int Main(string[] args){try{
  if(args.Length>0&&args[0]=="helper"){using(var audio=new AudioPlayer(0,0)){audio.Volume=.01f;audio.Enqueue(new byte[48000*8],CancellationToken.None);audio.Finish();Thread.Sleep(7000);}return 0;}
  int starts=0,ends=0;string failure=null;
  using(var audio=new AudioPlayer(0,.1)){audio.Paused=true;audio.Started=()=>Interlocked.Increment(ref starts);audio.Completed=()=>Interlocked.Increment(ref ends);audio.Failed=s=>failure=s;audio.Enqueue(new byte[48000/2],CancellationToken.None);audio.Finish();Thread.Sleep(350);Assert(starts==0&&ends==0&&audio.Paused,"paused queue retained without replay or completion");audio.Paused=false;for(int i=0;i<100&&ends==0&&failure==null;i++)Thread.Sleep(20);Assert(failure==null&&starts==1&&ends==1,"resume drains the same queue exactly once");}
  var info=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"helper"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
  using(var helper=Process.Start(info))try{Thread.Sleep(800);using(var duck=new AudioDucker()){
   duck.Probe();Assert(duck.Levels.ContainsKey((uint)helper.Id),"isolated audio session detected");float before=duck.Levels[(uint)helper.Id];Assert(duck.DuckOnly((uint)helper.Id)==1,"duck only the synthetic helper session");duck.Probe();Assert(Math.Abs(duck.Levels[(uint)helper.Id]-before*.2f)<.005,"media volume lowered to twenty percent");duck.Restore();duck.Probe();Assert(Math.Abs(duck.Levels[(uint)helper.Id]-before)<.005,"media volume restored");
  }}finally{if(!helper.HasExited)helper.Kill();}
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
