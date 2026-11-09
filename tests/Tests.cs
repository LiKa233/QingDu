using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using QingDu;

class Backend:IHotkeyBackend {
 public bool Fail; public List<int> Active=new List<int>(); public int Removed;
 public bool Register(int id,HotkeyBinding b){if(Fail)return false;Active.Add(id);return true;}
 public void Unregister(int id){Active.Remove(id);Removed++;}
}
class Mock:IDisposable {
 readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0); readonly Thread thread;
 public readonly List<string> Bodies=new List<string>(); public int Status=200,Delay;public byte[] Payload;public Uri Url;public Exception Failure;
 volatile bool stopped;
 public Mock(byte[] payload,int status=200,int delay=0){Payload=payload;Status=status;Delay=delay;listener.Start();Url=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/");thread=new Thread(Run);thread.IsBackground=true;thread.Start();}
 void Run(){while(!stopped){try{using(var client=listener.AcceptTcpClient())using(var s=client.GetStream()){
  var header=new List<byte>();int value;while((value=s.ReadByte())>=0){header.Add((byte)value);int n=header.Count;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}
  string h=Encoding.ASCII.GetString(header.ToArray());int length=0;foreach(string line in h.Split('\n'))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Split(':')[1]);
  byte[] body=new byte[length];int pos=0;while(pos<length){int got=s.Read(body,pos,length-pos);if(got==0)break;pos+=got;}lock(Bodies)Bodies.Add(Encoding.UTF8.GetString(body));
  if(Delay>0)Thread.Sleep(Delay);byte[] prefix=Encoding.ASCII.GetBytes("HTTP/1.1 "+Status+" Result\r\nContent-Type: "+(Payload.Length>0&&(Payload[0]==100||Payload[0]==58)?"text/event-stream":"audio/mpeg")+"\r\nContent-Length: "+Payload.Length+"\r\nConnection: close\r\n\r\n");s.Write(prefix,0,prefix.Length);
  for(int i=0;i<Payload.Length;i+=1907){int count=Math.Min(1907,Payload.Length-i);s.Write(Payload,i,count);s.Flush();Thread.Sleep(2);}
 }}catch(Exception e){if(!stopped&&!(e is IOException)&&!(e is SocketException))Failure=e;}}}
 public int Count {get{lock(Bodies)return Bodies.Count;}}
 public void Dispose(){stopped=true;listener.Stop();thread.Join(2500);if(Failure!=null)throw Failure;}
}
class Tests {
 static int checks;static string root,ffmpeg;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Throws(Action action,string label){bool threw=false;try{action();}catch{threw=true;}Check(threw,label);}
 static byte[] Tone(){byte[] pcm=new byte[96000];for(int i=0;i<48000;i++){short v=(short)(Math.Sin(i*2*Math.PI*440/24000)*8000);pcm[i*2]=(byte)v;pcm[i*2+1]=(byte)(v>>8);}return pcm;}
 static byte[] Sse(byte[] pcm,string finish="stop"){
  var result=new StringBuilder(": keepalive\n\n");for(int i=0;i<pcm.Length;i+=1907){byte[] part=pcm.Skip(i).Take(1907).ToArray();result.Append("data: {\"choices\":[{\"delta\":{\"audio\":{\"data\":\"").Append(Convert.ToBase64String(part)).Append("\"}}}]}\n\n");}
  result.Append("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"").Append(finish).Append("\"}],\"usage\":{\"completion_tokens\":7}}\n\ndata: [DONE]\n\n");return Encoding.UTF8.GetBytes(result.ToString());
 }
 static byte[] Decode(byte[] bytes,double speed,double pitch,bool raw){using(var m=new MemoryStream())using(var input=new MemoryStream(bytes)){AudioProcessor.Decode(input,pitch,ffmpeg,CancellationToken.None,b=>m.Write(b,0,b.Length),speed,raw);return m.ToArray();}}
 static double Frequency(byte[] pcm){int changes=0;for(int i=2;i<pcm.Length;i+=2){short a=(short)(pcm[i-2]|pcm[i-1]<<8),b=(short)(pcm[i]|pcm[i+1]<<8);if(a<=0&&b>0)changes++;}return changes/(pcm.Length/48000.0);}
 static void Hotkeys(){var b=new Backend();using(var h=new HotkeyManager(b)){string error;Check(h.TrySet(ReaderAction.Read,new HotkeyBinding(2,192),out error)&&h.TrySet(ReaderAction.Pause,new HotkeyBinding(3,80),out error)&&h.TrySet(ReaderAction.Stop,new HotkeyBinding(3,83),out error),"three default hotkeys");
  Check(!h.TrySet(ReaderAction.Read,new HotkeyBinding(3,80),out error)&&h.Binding(ReaderAction.Read).Key==192,"duplicate rejected and old binding retained");
  b.Fail=true;Check(!h.TrySet(ReaderAction.Read,new HotkeyBinding(3,82),out error)&&b.Removed==0&&h.Binding(ReaderAction.Read).Key==192,"OS conflict rolls back without unregistering old binding");b.Fail=false;
  Check(h.TrySet(ReaderAction.Read,new HotkeyBinding(3,82),out error)&&b.Removed==1,"successful hotkey replacement");
  foreach(var a in new[]{ReaderAction.Pause,ReaderAction.Stop})Check(h.TrySet(a,new HotkeyBinding(3,a==ReaderAction.Pause?81:84),out error),"independent editable "+a);
  foreach(var k in new[]{16,17,18,91,92,160,161,162,163,164,165})Check(!HotkeyManager.Valid(new HotkeyBinding(2,k)),"modifier rejected "+k);
  Check(!HotkeyManager.Valid(new HotkeyBinding(0,80)),"bare key rejected");ReaderAction action;Check(h.Resolve(b.Active[0],out action),"registered ID resolves");}Check(b.Active.Count==0,"all hotkeys released");}
 static void Settings(){var p=new Preferences();Check(p.Provider=="mimo"&&p.MimoVoice=="冰糖"&&p.FishModel==FishClient.FreeModel&&p.FishVoices.Count==0&&p.Speed==1&&p.Pitch==0&&p.Volume==100&&!p.Duck&&p.FishBufferMs==350&&p.MimoBufferMs==350&&p.MimoStyle=="","factory defaults");p.Save(root);p.PauseHotkey=new HotkeyBinding(3,81);p.ReadHotkey=new HotkeyBinding(3,82);p.StopHotkey=new HotkeyBinding(3,84);p.Save(root);var q=Preferences.Load(root);Check(q.ReadHotkey.Key==82&&q.PauseHotkey.Key==81&&q.StopHotkey.Key==84,"all shortcuts survive reload");
  string json=File.ReadAllText(Path.Combine(root,"settings.json"));Check(!json.Contains("Qwen")&&!json.Contains("Gpu")&&!json.Contains("Python"),"cloud-only configuration schema");
  foreach(string provider in new[]{"fish","mimo"}){if(provider=="fish")FishClient.SaveKey(root,"unit-test-placeholder");else MimoClient.SaveKey(root,"unit-test-placeholder");string key=provider=="fish"?FishClient.LoadKey(root):MimoClient.LoadKey(root);Check(key=="unit-test-placeholder"&&!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,provider+"-key.bin"))).Contains(key),provider+" DPAPI roundtrip without plaintext storage");}
  Check(MimoClient.Voices().Length==9&&MimoClient.Voices().Select(v=>v.id).Distinct().Count()==9,"nine distinct MiMo presets");
  Throws(()=>MimoClient.Request("placeholder",new Uri("https://example.org/")),"MiMo test endpoint restricted to loopback");Throws(()=>FishClient.CreateRequest("tts","POST","placeholder",FishClient.FreeModel,new Uri("https://example.org/")),"Fish test endpoint restricted to loopback");Throws(()=>FishClient.CreateRequest("tts","POST","placeholder","unknown",new Uri("http://localhost:1/")),"unknown Fish model refused");
 }
 static void Plans(){foreach(string input in new[]{"",new string('文',777),String.Concat(Enumerable.Repeat("清晨的阳光照进窗户，今天也要认真工作。下一句话保持完整。\n\n",30)),new string('a',219)+"😀"+new string('b',300),"小数3.14，时间00:20—00:24，比例37%。你好！”她说。"}){var plan=MimoPlan.Split(input);Check(String.Concat(plan)==input.Trim()&&plan.All(x=>x.Length<=220&&!Char.IsHighSurrogate(x[x.Length-1])&&!Char.IsLowSurrogate(x[0])),"sentence grouping conserves text and Unicode boundaries");}}
 static void Audio(){var previous=Console.InputEncoding;byte[] safe;try{Console.InputEncoding=new UTF8Encoding(true);safe=Decode(Tone(),1,0,true);}finally{Console.InputEncoding=previous;}Check(safe.Length==96000,"UTF-8 console preamble never enters binary audio pipe");byte[] pcm=Tone(),normal=Decode(pcm,1,0,true),fast=Decode(pcm,1.5,0,true),pitch=Decode(pcm,1,3,true);Check(normal.Length==pcm.Length,"raw PCM decode exact duration");Check(fast.Length>pcm.Length*.62&&fast.Length<pcm.Length*.71&&Math.Abs(Frequency(fast)-440)<8,"atempo duration and preserved pitch");Check(pitch.Length>pcm.Length*.95&&pitch.Length<pcm.Length*1.05&&Math.Abs(Frequency(pitch)-523.25)<12,"rubberband pitch and preserved duration");byte[] mp3=File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tone.mp3"));Check(Decode(mp3,1,0,false).Length>90000,"MP3 decoding and resampling");Throws(()=>Decode(new byte[]{1,2,3},1,0,false),"invalid MP3 rejected");var token=new CancellationTokenSource();token.Cancel();Throws(()=>AudioProcessor.Decode(new MemoryStream(pcm),0,ffmpeg,token.Token,b=>{}),"audio decode cancellation");}
 static void Streaming(){byte[] pcm=Tone(),sse=Sse(pcm);using(var output=new MemoryStream()){foreach(var part in MimoClient.ReadAudio(new StringReader(Encoding.UTF8.GetString(sse)),CancellationToken.None))output.Write(part,0,part.Length);Check(pcm.SequenceEqual(output.ToArray()),"SSE odd chunks assembled exactly without silence");}
  foreach(string reason in new[]{"length","repetition_truncation","content_filter"})Throws(()=>{foreach(var part in MimoClient.ReadAudio(new StringReader(Encoding.UTF8.GetString(Sse(pcm,reason))),CancellationToken.None)){}},"abnormal finish stops: "+reason);
  Throws(()=>{foreach(var part in MimoClient.ReadAudio(new StringReader("data: {}\n\n"),CancellationToken.None)){}},"truncated SSE rejected");
  foreach(double speed in new[]{1.0,1.3})using(var server=new Mock(sse)){long count=0;int packets=0;MimoClient.Speak("unit-test-placeholder","冰糖","这是无隐私的合成测试。","",speed,0,ffmpeg,CancellationToken.None,b=>{count+=b.Length;packets++;},server.Url);Check(count>pcm.Length*.92/speed&&count<pcm.Length*1.08/speed&&packets>1&&server.Count==1,"MiMo streaming speed "+speed);}
  string text=String.Concat(Enumerable.Repeat("春天的微风吹过公园。小鸟在树枝上唱歌。",24));int segments=MimoPlan.Split(text).Count;using(var server=new Mock(sse)){int traces=0;long bytes=0;foreach(var b in MimoClient.StreamPlanned("unit-test-placeholder","冰糖",text,"",CancellationToken.None,server.Url,s=>{Check(s.Done&&s.FinishReason=="stop","segment termination logged");traces++;}))bytes+=b.Length;var sent=server.Bodies.Select(x=>new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(x)).Select(x=>((System.Collections.IEnumerable)x["messages"]).Cast<object>().ToArray()).Select(x=>(Dictionary<string,object>)x[x.Length-1]).Select(x=>(string)x["content"]);Check(server.Count==segments&&traces==segments&&bytes==segments*pcm.Length&&String.Concat(sent)==text,"sequential complete segments, no repeat or fixed silence");}
  using(var server=new Mock(sse))using(var token=new CancellationTokenSource()){Throws(()=>{foreach(var b in MimoClient.StreamPlanned("unit-test-placeholder","冰糖",text,"",token.Token,server.Url,s=>token.Cancel())){}},"stop cancels sequence");Check(server.Count==1,"stop prevents next segment request");}
  foreach(int status in new[]{401,402,429,503})using(var server=new Mock(new byte[0],status)){Throws(()=>{foreach(var b in MimoClient.StreamPlanned("unit-test-placeholder","冰糖",text,"",CancellationToken.None,server.Url)){}},"MiMo HTTP "+status);Check(server.Count==1,"no retry after HTTP failure");}
  using(var server=new Mock(sse,200,1200))using(var token=new CancellationTokenSource(150)){var watch=Stopwatch.StartNew();Throws(()=>MimoClient.Speak("unit-test-placeholder","冰糖","测试。","",1,0,ffmpeg,token.Token,b=>{},server.Url),"cancel pending HTTP");Check(watch.ElapsedMilliseconds<1000,"prompt HTTP cancellation");}
  byte[] mp3=File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tone.mp3"));using(var server=new Mock(mp3)){long count=0;FishClient.Speak("unit-test-placeholder",FishClient.FreeModel,"0123456789abcdef0123456789abcdef","这是测试。",1,0,ffmpeg,CancellationToken.None,b=>count+=b.Length,server.Url);Check(count>90000&&server.Count==1,"Fish streaming MP3 decode");}
  using(var server=new Mock(new byte[0],402)){Throws(()=>FishClient.Speak("unit-test-placeholder",FishClient.FreeModel,"0123456789abcdef0123456789abcdef","测试。",1,0,ffmpeg,CancellationToken.None,b=>{},server.Url),"Fish quota error");Check(server.Count==1,"Fish never falls back to paid");}
 }
 public static int Main(string[] args){try{root=args[0];Directory.CreateDirectory(root);if(args.Length>1&&args[1]=="defaults"){new Preferences().Save(root);return 0;}ffmpeg=args[1];Hotkeys();Settings();Plans();Audio();Streaming();Console.WriteLine("SUCCESS "+checks+" assertions");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
