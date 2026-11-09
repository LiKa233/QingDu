using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;

namespace QingDu {
public static class MimoClient {
    public const string Model="mimo-v2.5-tts";
    public const string Documentation="https://mimo.mi.com/docs/zh-CN/quick-start/usage-guide/audio/speech-synthesis-v2.5";
    public static Voice[] Voices(){return new[]{
        new Voice{id="mimo_default",name="MiMo 默认（中国集群冰糖 / 其他集群 Mia）"},
        new Voice{id="冰糖",name="冰糖 · 中文女声"},new Voice{id="茉莉",name="茉莉 · 中文女声"},
        new Voice{id="苏打",name="苏打 · 中文男声"},new Voice{id="白桦",name="白桦 · 中文男声"},
        new Voice{id="Mia",name="Mia · 英文女声"},new Voice{id="Chloe",name="Chloe · 英文女声"},
        new Voice{id="Milo",name="Milo · 英文男声"},new Voice{id="Dean",name="Dean · 英文男声"}};}
    public static bool ValidVoice(string value){foreach(var voice in Voices())if(voice.id==value)return true;return false;}
    public static string LoadKey(string data){string path=Path.Combine(data,"mimo-key.bin");if(!File.Exists(path))return "";try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));}catch{throw new MimoException("无法解密 MiMo Key，请在本机重新填写并保存。");}}
    public static void SaveKey(string data,string key){string path=Path.Combine(data,"mimo-key.bin");if(String.IsNullOrWhiteSpace(key)){if(File.Exists(path))File.Delete(path);return;}byte[] raw=Encoding.UTF8.GetBytes(key.Trim());try{byte[] encrypted=ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser);File.WriteAllBytes(path+".tmp",encrypted);if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}finally{Array.Clear(raw,0,raw.Length);}}
    public static byte[] Body(string text,string voice,string style){
        if(!ValidVoice(voice))throw new MimoException("请选择文档中的 MiMo 预设音色。");
        var messages=new List<object>();if(!String.IsNullOrWhiteSpace(style))messages.Add(new {role="user",content=style.Trim()});
        messages.Add(new {role="assistant",content=text.Trim()});
        return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {model=Model,messages=messages,audio=new{format="pcm16",voice=voice},stream=true}));
    }
    public static HttpWebRequest Request(string apiKey,Uri testBase=null){
        if(String.IsNullOrWhiteSpace(apiKey)||apiKey.IndexOfAny(new[]{'\r','\n'})>=0)throw new MimoException("请先在 MiMo 设置填写有效 API Key。");
        if(testBase!=null&&!testBase.IsLoopback)throw new MimoException("测试地址必须是本机。");
        var req=(HttpWebRequest)WebRequest.Create(new Uri(testBase??new Uri("https://api.xiaomimimo.com/"),"v1/chat/completions"));req.Method="POST";req.Headers["api-key"]=apiKey.Trim();req.ContentType="application/json";req.Accept="text/event-stream";req.AllowAutoRedirect=false;req.Timeout=60000;req.ReadWriteTimeout=60000;req.ServicePoint.Expect100Continue=false;req.AllowWriteStreamBuffering=false;req.UserAgent="QingDu/"+AppVersion.Value;req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;return req;
    }
    public static void Speak(string apiKey,string voice,string text,string style,double speed,double pitch,string ffmpeg,CancellationToken token,Action<byte[]> output,Uri testBase=null,Action<MimoStreamStats> trace=null,Action<int,int> segmentStarted=null){
        var chunks=StreamPlanned(apiKey,voice,text,style,token,testBase,trace,segmentStarted);
        if(Math.Abs(speed-1)<.001&&Math.Abs(pitch)<.001){foreach(var pcm in chunks){token.ThrowIfCancellationRequested();output(pcm);}}
        else using(var input=new ChunkStream(chunks,token))AudioProcessor.Decode(input,pitch,ffmpeg,token,output,speed,true);
    }
    public static IEnumerable<byte[]> StreamPlanned(string apiKey,string voice,string text,string style,CancellationToken token,Uri testBase=null,Action<MimoStreamStats> trace=null,Action<int,int> segmentStarted=null){
        var segments=MimoPlan.Split(text);for(int index=0;index<segments.Count;index++){
            token.ThrowIfCancellationRequested();if(segmentStarted!=null)segmentStarted(index+1,segments.Count);
            var stats=new MimoStreamStats{Segment=index+1,Segments=segments.Count,Characters=segments[index].Length};
            foreach(var pcm in StreamSingle(apiKey,voice,segments[index],style,token,testBase,stats,trace))yield return pcm;
        }
    }
    public static IEnumerable<byte[]> StreamSingle(string apiKey,string voice,string text,string style,CancellationToken token,Uri testBase=null,MimoStreamStats stats=null,Action<MimoStreamStats> trace=null){
        stats=stats??new MimoStreamStats{Segment=1,Segments=1,Characters=text.Length};var watch=Stopwatch.StartNew();
        byte[] body=Body(text,voice,style);var req=Request(apiKey,testBase);req.ContentLength=body.Length;
        try{using(token.Register(()=>req.Abort())){
            token.ThrowIfCancellationRequested();using(var stream=req.GetRequestStream())stream.Write(body,0,body.Length);
            using(var response=(HttpWebResponse)req.GetResponse()){
                if(response.StatusCode!=HttpStatusCode.OK)throw new MimoException(StatusError((int)response.StatusCode));
                if(!(response.ContentType??"").StartsWith("text/event-stream",StringComparison.OrdinalIgnoreCase))throw new MimoException("MiMo 未返回流式音频，请检查接口与模型权限。");
                using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))foreach(var pcm in ReadAudio(reader,token,stats)){if(stats.FirstAudioMs<0)stats.FirstAudioMs=watch.ElapsedMilliseconds;yield return pcm;}
            }
        }}finally{stats.ElapsedMs=watch.ElapsedMilliseconds;if(trace!=null)trace(stats);}
    }
    static IEnumerable<string> Events(TextReader reader,CancellationToken token){
        var data=new StringBuilder();string line;while((line=reader.ReadLine())!=null){token.ThrowIfCancellationRequested();if(line.Length==0){if(data.Length>0){yield return data.ToString();data.Length=0;}continue;}if(line.StartsWith(":"))continue;if(line.StartsWith("data:")){string value=line.Substring(5);if(value.StartsWith(" "))value=value.Substring(1);if(data.Length>0)data.Append('\n');data.Append(value);if(data.Length>16000000)throw new MimoException("MiMo 流式数据块过大，已停止。");}}
        if(data.Length>0)yield return data.ToString();
    }
    public static IEnumerable<byte[]> ReadAudio(TextReader reader,CancellationToken token,MimoStreamStats stats=null){
        stats=stats??new MimoStreamStats();var seen=new HashSet<string>();
        bool complete=false;long total=0;int carry=-1;
        foreach(string data in Events(reader,token)){
            if(data.Trim()=="[DONE]"){stats.Done=true;complete=true;break;}
            Dictionary<string,object> item;try{item=new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<Dictionary<string,object>>(data);}catch{throw new MimoException("MiMo 流式响应格式错误。");}
            if(item==null)continue;object usageRaw;if(item.TryGetValue("usage",out usageRaw)){var usage=usageRaw as Dictionary<string,object>;object count;long value;if(usage!=null){if(usage.TryGetValue("completion_tokens",out count)&&Int64.TryParse(Convert.ToString(count),out value))stats.CompletionTokens=value;if(usage.TryGetValue("prompt_tokens",out count)&&Int64.TryParse(Convert.ToString(count),out value))stats.PromptTokens=value;}}if(item.ContainsKey("error"))throw new MimoException("MiMo 在流式生成中返回错误，请检查权限、额度或文本长度。");
            object choices;if(!item.TryGetValue("choices",out choices)||!(choices is System.Collections.IEnumerable))continue;
            foreach(var raw in (System.Collections.IEnumerable)choices){
                var choice=raw as Dictionary<string,object>;if(choice==null)continue;object finish;
                if(choice.TryGetValue("finish_reason",out finish)&&finish!=null){string reason=Convert.ToString(finish);stats.FinishReason=reason=="stop"||reason=="length"||reason=="content_filter"||reason=="repetition_truncation"?reason:"other";if(reason=="length")throw new MimoException("MiMo 达到生成长度上限，已停止；本段尚未确认读完，未从头重试。");if(reason=="content_filter")throw new MimoException("MiMo 内容审核中止生成，未继续后续段落。");if(reason=="repetition_truncation")throw new MimoException("MiMo 检测到生成重复，已停止；未从头重试。");if(reason!="stop")throw new MimoException("MiMo 提前结束生成，本段尚未确认读完；未从头重试。");complete=true;}
                object deltaRaw;if(!choice.TryGetValue("delta",out deltaRaw))continue;var delta=deltaRaw as Dictionary<string,object>;object audioRaw;if(delta==null||!delta.TryGetValue("audio",out audioRaw))continue;
                var audio=audioRaw as Dictionary<string,object>;object encoded;if(audio==null||!audio.TryGetValue("data",out encoded)||encoded==null||String.IsNullOrEmpty(Convert.ToString(encoded)))continue;
                byte[] decoded;try{decoded=Convert.FromBase64String(Convert.ToString(encoded));}catch{throw new MimoException("MiMo 音频编码错误。");}
                stats.AudioChunks++;stats.AudioBytes+=decoded.Length;if(decoded.Length>1024){bool nonzero=false;foreach(byte sample in decoded)if(sample!=0){nonzero=true;break;}if(nonzero){using(var hash=SHA256.Create()){string fingerprint=Convert.ToBase64String(hash.ComputeHash(decoded));if(!seen.Add(fingerprint))stats.DuplicateAudioChunks++;}}}
                int size=decoded.Length+(carry>=0?1:0);var joined=new byte[size];int offset=0;if(carry>=0){joined[0]=(byte)carry;offset=1;}Buffer.BlockCopy(decoded,0,joined,offset,decoded.Length);int even=size&~1;carry=size>even?joined[even]:-1;
                if(even>0){var pcm=new byte[even];Buffer.BlockCopy(joined,0,pcm,0,even);total+=even;yield return pcm;}
            }
        }
        token.ThrowIfCancellationRequested();if(!complete)throw new MimoException("MiMo 生成连接提前结束，请手动重试。");if(carry>=0)throw new MimoException("MiMo 音频末尾不完整。");if(total==0)throw new MimoException("MiMo 未生成可播放音频。");
    }
    public static string Error(Exception ex){
        var web=ex as WebException;var response=web==null?null:web.Response as HttpWebResponse;if(response!=null){int status=(int)response.StatusCode;response.Close();return StatusError(status);}
        if(ex is MimoException||ex is AudioProcessingException)return ex.Message;if(ex is OperationCanceledException)return "已停止";
        if(web!=null)return "MiMo 连接中断或超时，请检查网络后手动重试。";
        return "MiMo 音频处理失败，请检查配置或重启应用。";
    }
    public static string StatusError(int status){switch(status){case 401:case 403:return "MiMo Key 无效、未获模型权限或账号尚未完成必要认证，请在控制台检查。";case 402:return "MiMo 余额或额度不足，请在控制台检查；未自动充值或切换服务。";case 429:return "MiMo 请求过于频繁或额度受限，请稍后重试。";case 400:case 422:return "MiMo 不接受当前音色、文本或参数，请缩短文本并检查音色。";case 404:return "MiMo 模型或接口不存在，请核对官方服务状态。";case 503:return "MiMo 服务繁忙，请稍后重试。";default:return "MiMo 请求失败（HTTP "+status+"）；未自动重试或切换服务。";}}
}
public sealed class MimoException:Exception {public MimoException(string message):base(message){}}
public sealed class ChunkStream:Stream {
    readonly IEnumerator<byte[]> chunks;readonly CancellationToken token;byte[] current;int offset;
    public ChunkStream(IEnumerable<byte[]> source,CancellationToken cancellation){chunks=source.GetEnumerator();token=cancellation;}
    public override int Read(byte[] buffer,int start,int count){if(count==0)return 0;token.ThrowIfCancellationRequested();while(current==null||offset>=current.Length){if(!chunks.MoveNext())return 0;current=chunks.Current;offset=0;}int n=Math.Min(count,current.Length-offset);Buffer.BlockCopy(current,offset,buffer,start,n);offset+=n;return n;}
    protected override void Dispose(bool disposing){if(disposing)chunks.Dispose();base.Dispose(disposing);}public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}public override void Flush(){}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
}
public sealed class MimoSettingsForm:Form {
    public MimoSettingsForm(Preferences prefs,string data){
        Text="MiMo 设置";ClientSize=new Size(650,440);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1};Controls.Add(root);int[] heights={30,38,55,30,115,55,50};foreach(int h in heights)root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        root.Controls.Add(new Label{Text="MiMo API Key（独立保存，Windows 当前用户加密）",AutoSize=true},0,0);
        var key=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Fill,AccessibleName="MiMo API Key"};try{key.Text=MimoClient.LoadKey(data);}catch{}root.Controls.Add(key,0,1);
        root.Controls.Add(new Label{Text="固定模型 mimo-v2.5-tts · 文档全部9个预设已加入主窗口。\n官方当前标注限时免费，政策可能调整；文本会发送至小米 MiMo。\n语速和音调使用本机连续处理；默认值直接播放原始 PCM。",Dock=DockStyle.Fill,ForeColor=Color.DimGray},0,2);
        root.Controls.Add(new Label{Text="可选风格指令（留空使用所选预设的自然风格）",AutoSize=true},0,3);
        var style=new TextBox{Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=prefs.MimoStyle??"",MaxLength=1000,AccessibleName="MiMo 风格指令"};root.Controls.Add(style,0,4);
        var links=new FlowLayoutPanel{Dock=DockStyle.Fill};foreach(var entry in new[]{new[]{"官方使用文档",MimoClient.Documentation},new[]{"API 定价","https://mimo.mi.com/docs/zh-CN/price/pay-as-you-go"},new[]{"控制台","https://platform.xiaomimimo.com/"}}){string linkUrl=entry[1];var link=new LinkLabel{Text=entry[0],AutoSize=true,Margin=new Padding(0,10,18,0)};link.LinkClicked+=(s,e)=>{try{Process.Start(linkUrl);}catch{}};links.Controls.Add(link);}root.Controls.Add(links,0,5);
        var end=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="保存",Width=95,Height=34};save.Click+=(s,e)=>{try{MimoClient.SaveKey(data,key.Text);prefs.MimoStyle=style.Text.Trim();DialogResult=DialogResult.OK;Close();}catch{MessageBox.Show(this,"无法加密保存 Key，请检查本机用户环境。","轻读");}};end.Controls.Add(save);var back=new Button{Text="取消",Width=95,Height=34,DialogResult=DialogResult.Cancel};end.Controls.Add(back);root.Controls.Add(end,0,6);CancelButton=back;FormClosed+=(s,e)=>key.Clear();
    }
}
}
