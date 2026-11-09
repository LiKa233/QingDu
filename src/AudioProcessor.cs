using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
namespace QingDu {
public sealed class AudioProcessingException:Exception{public AudioProcessingException(string message):base(message){}}
public static class AudioProcessor {
    public static void Decode(Stream input,double pitch,string ffmpeg,CancellationToken token,Action<byte[]> output,double speed=1,bool rawPcm=false) {
        token.ThrowIfCancellationRequested();
        var filters=new List<string>();
        if(Math.Abs(pitch)>.001)filters.Add("rubberband=tempo=1:pitch="+Math.Pow(2,pitch/12).ToString("F8",CultureInfo.InvariantCulture)+":formant=preserved:pitchq=quality");
        if(Math.Abs(speed-1)>.001)filters.Add("atempo="+speed.ToString("F8",CultureInfo.InvariantCulture));
        string filter=filters.Count==0?"":" -af "+String.Join(",",filters.ToArray());
        string inputFormat=rawPcm?"-f s16le -ar 24000 -ac 1":"-f mp3";
        string pipeName="QingDu-audio-"+Guid.NewGuid().ToString("N");
        var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
        // Framework redirected stdin uses an auto-flushing text writer, whose BOM can
        // enter binary audio even before BaseStream is accessed. Avoid it entirely.
        using(var pipe=new NamedPipeServerStream(pipeName,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security)){
            var info=new ProcessStartInfo(ffmpeg,"-hide_banner -nostdin -loglevel error -probesize 32 -analyzeduration 0 "+inputFormat+" -i \"\\\\.\\pipe\\"+pipeName+"\""+filter+" -f s16le -acodec pcm_s16le -ar 24000 -ac 1 -flush_packets 1 pipe:1") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(info))using(token.Register(()=>{try{pipe.Dispose();}catch{}try{if(!p.HasExited)p.Kill();}catch{}})){
                p.ErrorDataReceived+=(s,e)=>{};p.BeginErrorReadLine();long bytes=0;
                p.Exited+=(s,e)=>{try{pipe.Dispose();}catch{}};p.EnableRaisingEvents=true;
                var consumer=Task.Run(()=>{try{byte[] buffer=new byte[8193];int carry=0,n;while((n=p.StandardOutput.BaseStream.Read(buffer,carry,8192))>0){token.ThrowIfCancellationRequested();int size=n+carry;int even=size&~1;if(even>0){var chunk=new byte[even];Buffer.BlockCopy(buffer,0,chunk,0,even);output(chunk);bytes+=even;}carry=size-even;if(carry>0)buffer[0]=buffer[even];}if(carry!=0)throw new AudioProcessingException("音频数据不完整，请重新朗读。");}catch{try{p.Kill();}catch{}throw;}});
                try{
                    var connection=pipe.BeginWaitForConnection(null,null);
                    using(var ready=connection.AsyncWaitHandle){int result=WaitHandle.WaitAny(new[]{ready,token.WaitHandle},10000);token.ThrowIfCancellationRequested();if(result==WaitHandle.WaitTimeout)throw new AudioProcessingException("音频组件连接超时。");pipe.EndWaitForConnection(connection);}
                    byte[] buffer=new byte[8192];int n;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();pipe.Write(buffer,0,n);pipe.Flush();}
                    pipe.Dispose();consumer.GetAwaiter().GetResult();
                    if(!p.WaitForExit(10000)){p.Kill();throw new AudioProcessingException("音频解码超时。");}
                    token.ThrowIfCancellationRequested();if(p.ExitCode!=0||bytes==0)throw new AudioProcessingException("未收到可播放的完整音频，请检查音色与网络。");
                }catch{try{pipe.Dispose();}catch{}try{if(!p.HasExited)p.Kill();}catch{}try{consumer.GetAwaiter().GetResult();}catch{}throw;}
            }
        }
    }
}
}
