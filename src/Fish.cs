using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;

namespace QingDu {
public static class FishClient {
    public const string FreeModel="s2.1-pro-free", PaidModel="s2.1-pro";
    public static string LoadKey(string data) {
        string path=Path.Combine(data,"fish-key.bin");if(!File.Exists(path))return "";
        try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));}
        catch{throw new Exception("无法解密 Fish Key，请在本机重新填写并保存。");}
    }
    public static void SaveKey(string data,string key) {
        string path=Path.Combine(data,"fish-key.bin");
        if(String.IsNullOrWhiteSpace(key)){if(File.Exists(path))File.Delete(path);return;}
        byte[] raw=Encoding.UTF8.GetBytes(key.Trim());
        try{byte[] encrypted=ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser);File.WriteAllBytes(path+".tmp",encrypted);if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
        finally{Array.Clear(raw,0,raw.Length);}
    }
    public static string VoiceId(string value) {
        value=(value??"").Trim();Uri u;
        if(Uri.TryCreate(value,UriKind.Absolute,out u)){
            if(u.Scheme!="https"||!(u.Host=="fish.audio"||u.Host=="www.fish.audio"))throw new Exception("请填写 Fish 官方音色页面链接或音色 ID。");
            var match=Regex.Match(u.AbsolutePath,@"(?:^|/)([a-fA-F0-9]{32})(?:/|$)");value=match.Success?match.Groups[1].Value:"";
        }
        if(!Regex.IsMatch(value,@"\A[a-fA-F0-9]{32}\z"))throw new Exception("音色 ID 应为32位字母数字编号，可直接粘贴 Fish 音色页面链接。");
        return value.ToLowerInvariant();
    }
    public static HttpWebRequest CreateRequest(string endpoint,string method,string apiKey,string model=null,Uri testBase=null) {
        if(String.IsNullOrWhiteSpace(apiKey)||apiKey.IndexOfAny(new[]{'\r','\n'})>=0)throw new Exception("请先在 Fish 设置中填写有效的 API Key。");
        // Unknown model headers can silently select a paid tier at the vendor. Never pass arbitrary values.
        if(model!=null&&model!=FreeModel&&model!=PaidModel)throw new Exception("不支持此 Fish 模型，请重新选择；未发送请求。");
        if(model==FreeModel&&testBase==null&&DateTime.UtcNow.Date>new DateTime(2026,11,30))throw new FishAudioException("本版本免费档有效期已到，请核对 Fish 最新政策并更新应用；未发送请求。");
        Uri baseUri=testBase??new Uri("https://api.fish.audio/");
        if(testBase!=null&&!testBase.IsLoopback)throw new Exception("测试服务必须是本机地址。");
        var req=(HttpWebRequest)WebRequest.Create(new Uri(baseUri,endpoint));req.Method=method;req.AllowAutoRedirect=false;req.Timeout=30000;req.ReadWriteTimeout=60000;req.KeepAlive=true;
        req.Headers[HttpRequestHeader.Authorization]="Bearer "+apiKey.Trim();if(model!=null)req.Headers["model"]=model;
        req.UserAgent="QingDu/"+AppVersion.Value;req.ServicePoint.Expect100Continue=false;req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        return req;
    }
    public static byte[] Body(string text,string reference,double speed) {
        return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {text=text.Trim(),reference_id=VoiceId(reference),format="mp3",sample_rate=44100,mp3_bitrate=128,latency="normal",prosody=new {speed=speed,volume=0,normalize_loudness=true}}));
    }
    public static string Error(Exception e) {
        var web=e as WebException;var response=web==null?null:web.Response as HttpWebResponse;
        if(response!=null){int code=(int)response.StatusCode;response.Close();return StatusError(code);}
        if(e is OperationCanceledException)return "已停止";
        if(web!=null)return "Fish 连接中断或超时，请检查网络后重试（未自动重试或切换付费）。";
        // Do not display vendor response bodies, headers or credential-bearing diagnostics.
        return e is FishAudioException||e is AudioProcessingException?e.Message:"Fish 音频处理失败，请检查网络、音色 ID 或重启应用。";
    }
    public static string StatusError(int code) {
        switch(code){case 401:case 403:return "Fish Key 无效或无权使用该音色，请在设置中检查。";
        case 402:return "Fish 拒绝请求：余额或额度不足；未切换模型、未自动充值。";
        case 404:return "Fish 音色或接口不存在，请检查音色 ID。";
        case 429:return "Fish 请求过于频繁或免费额度受限，请稍后手动重试。";
        case 400:case 422:return "Fish 不接受当前文本或音色配置，请检查音色 ID 与模型。";
        case 503:return "Fish 服务繁忙，请稍后手动重试。";
        default:return "Fish 请求失败（HTTP "+code+"）；未自动重试或切换模型。";}
    }
    public static void TestKey(string apiKey,CancellationToken token,Uri testBase=null) {
        var req=CreateRequest("wallet/self/api-credit","GET",apiKey,null,testBase);
        using(token.Register(()=>req.Abort()))using(var resp=(HttpWebResponse)req.GetResponse()){
            if(resp.StatusCode!=HttpStatusCode.OK)throw new FishAudioException(StatusError((int)resp.StatusCode));
            using(var reader=new StreamReader(resp.GetResponseStream())){var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(result==null||!result.ContainsKey("credit"))throw new FishAudioException("账户接口返回异常，请稍后重试。");}
        }
    }
    public static void Speak(string apiKey,string model,string reference,string text,double speed,double pitch,string ffmpeg,CancellationToken token,Action<byte[]> output,Uri testBase=null) {
        var req=CreateRequest("v1/tts","POST",apiKey,model,testBase);byte[] raw=Body(text,reference,speed);
        req.ContentType="application/json";req.Accept="audio/mpeg";req.ContentLength=raw.Length;req.AllowWriteStreamBuffering=false;
        using(token.Register(()=>req.Abort())){
            token.ThrowIfCancellationRequested();using(var stream=req.GetRequestStream())stream.Write(raw,0,raw.Length);
            using(var resp=(HttpWebResponse)req.GetResponse()){
                if(resp.StatusCode!=HttpStatusCode.OK)throw new FishAudioException(StatusError((int)resp.StatusCode));
                string type=resp.ContentType??"";if(type.Contains("json")||type.Contains("text/"))throw new FishAudioException("Fish 未返回音频，请检查当前模型和音色。");
                using(var input=resp.GetResponseStream())AudioProcessor.Decode(input,pitch,ffmpeg,token,output);
            }
        }
    }

}
public sealed class FishAudioException:Exception {public FishAudioException(string message):base(message){}}

public sealed class FishSettingsForm:Form {
    readonly Preferences prefs;readonly string data;readonly TextBox keyBox=new TextBox(),nameBox=new TextBox(),idBox=new TextBox();readonly ComboBox modelBox=new ComboBox();readonly ListBox voices=new ListBox();readonly Label result=new Label();readonly CancellationTokenSource cancel=new CancellationTokenSource();
    public FishSettingsForm(Preferences settings,string dataPath) {
        prefs=settings;data=dataPath;Text="Fish Audio 设置";ClientSize=new Size(650,710);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=12};Controls.Add(root);
        int[] heights={28,36,28,38,62,28,125,35,37,45,45,48};foreach(int h in heights)root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        root.Controls.Add(new Label{Text="API Key（仅保存在本机，Windows 用户级加密）",AutoSize=true},0,0);
        keyBox.UseSystemPasswordChar=true;keyBox.Dock=DockStyle.Fill;keyBox.AccessibleName="Fish API Key";try{keyBox.Text=FishClient.LoadKey(data);}catch{result.Text="原 Key 无法解密，请重新填写。";}root.Controls.Add(keyBox,0,1);
        root.Controls.Add(new Label{Text="模型（免费失败时不会自动切换付费）",AutoSize=true},0,2);
        modelBox.DropDownStyle=ComboBoxStyle.DropDownList;modelBox.Dock=DockStyle.Fill;modelBox.Items.AddRange(new object[]{"S2.1 Pro Free · 免费","S2.1 Pro · 付费 $15 / 百万 UTF-8 字节"});modelBox.SelectedIndex=prefs.FishModel==FishClient.PaidModel?1:0;root.Controls.Add(modelBox,0,3);
        root.Controls.Add(new Label{Text="免费档当前开放至 2026-11-30；请求可能被 Fish 留存并用于改进模型。\n付费档约每10万纯汉字 $4.50。网站会员与 API 额度分开计费。\n点击朗读会将所选文本发送到 Fish Audio。",Dock=DockStyle.Fill,ForeColor=Color.DimGray},0,4);
        var links=new FlowLayoutPanel{Dock=DockStyle.Fill};AddLink(links,"获取 API Key","https://fish.audio/app/developers/");AddLink(links,"打开音色库","https://fish.audio/");AddLink(links,"免费档说明","https://fish.audio/blog/s2-1-pro-free-api/");root.Controls.Add(links,0,5);
        voices.Dock=DockStyle.Fill;voices.AccessibleName="已保存的 Fish 音色";foreach(var v in prefs.FishVoices)voices.Items.Add(new Voice{id=v.id,name=v.name});voices.SelectedIndexChanged+=(s,e)=>{var v=voices.SelectedItem as Voice;if(v!=null){nameBox.Text=v.name;idBox.Text=v.id;}};root.Controls.Add(voices,0,6);
        var names=new FlowLayoutPanel{Dock=DockStyle.Fill};names.Controls.Add(new Label{Text="音色名称",AutoSize=true,Margin=new Padding(0,5,5,0)});nameBox.Width=490;nameBox.AccessibleName="Fish 音色名称";names.Controls.Add(nameBox);root.Controls.Add(names,0,7);
        var ids=new FlowLayoutPanel{Dock=DockStyle.Fill};ids.Controls.Add(new Label{Text="ID / 链接",AutoSize=true,Margin=new Padding(0,5,5,0)});idBox.Width=490;idBox.AccessibleName="Fish 音色 ID 或链接";ids.Controls.Add(idBox);root.Controls.Add(ids,0,8);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};var add=new Button{Text="添加 / 更新音色",AutoSize=true};add.Click+=(s,e)=>{try{AddVoice();result.Text="音色已加入列表；点保存后生效。";}catch(Exception ex){result.Text=ex.Message;}};actions.Controls.Add(add);var remove=new Button{Text="移除选中音色",AutoSize=true};remove.Click+=(s,e)=>{if(voices.SelectedIndex>=0)voices.Items.RemoveAt(voices.SelectedIndex);nameBox.Clear();idBox.Clear();};actions.Controls.Add(remove);var test=new Button{Text="测试 Key（不合成）",AutoSize=true};test.Click+=async(s,e)=>{test.Enabled=false;result.Text="正在验证账户接口…";string k=keyBox.Text;try{await Task.Run(()=>FishClient.TestKey(k,cancel.Token));if(!IsDisposed)result.Text="Key 验证成功；音色权限需实际朗读确认。";}catch(Exception ex){if(!IsDisposed&&!cancel.IsCancellationRequested)result.Text=FishClient.Error(ex);}finally{if(!IsDisposed)test.Enabled=true;}};actions.Controls.Add(test);root.Controls.Add(actions,0,9);
        result.Dock=DockStyle.Fill;result.ForeColor=Color.FromArgb(24,95,130);root.Controls.Add(result,0,10);
        var end=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="保存",Width=95,Height=34};save.Click+=(s,e)=>{try{if(!String.IsNullOrWhiteSpace(idBox.Text))AddVoice();FishClient.SaveKey(data,keyBox.Text);prefs.FishModel=modelBox.SelectedIndex==1?FishClient.PaidModel:FishClient.FreeModel;prefs.FishVoices.Clear();foreach(Voice v in voices.Items)prefs.FishVoices.Add(v);var selected=voices.SelectedItem as Voice;if(selected!=null)prefs.FishVoice=selected.id;else if(prefs.FishVoices.Count>0)prefs.FishVoice=prefs.FishVoices[0].id;else prefs.FishVoice="";DialogResult=DialogResult.OK;Close();}catch(Exception ex){result.Text=ex.Message;}};end.Controls.Add(save);var back=new Button{Text="取消",Width=95,Height=34,DialogResult=DialogResult.Cancel};end.Controls.Add(back);root.Controls.Add(end,0,11);CancelButton=back;
        for(int i=0;i<voices.Items.Count;i++)if(((Voice)voices.Items[i]).id==prefs.FishVoice)voices.SelectedIndex=i;
        FormClosed+=(s,e)=>{cancel.Cancel();keyBox.Clear();};
    }
    void AddVoice(){string id=FishClient.VoiceId(idBox.Text);string name=String.IsNullOrWhiteSpace(nameBox.Text)?"Fish · "+id.Substring(0,8):nameBox.Text.Trim();int index=-1;for(int i=0;i<voices.Items.Count;i++)if(((Voice)voices.Items[i]).id==id)index=i;var v=new Voice{id=id,name=name};if(index>=0){voices.Items[index]=v;voices.SelectedIndex=index;}else{voices.Items.Add(v);voices.SelectedIndex=voices.Items.Count-1;}}
    static void AddLink(Control parent,string label,string url){var link=new LinkLabel{Text=label,AutoSize=true,Margin=new Padding(0,0,18,0)};link.LinkClicked+=(s,e)=>{try{Process.Start(url);}catch{}};parent.Controls.Add(link);}
}
}
