using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QingDu {
public sealed class ReaderForm:Form {
    readonly string app=AppDomain.CurrentDomain.BaseDirectory,data;
    readonly TextBox text=new TextBox();readonly ComboBox provider=new ComboBox(),voice=new ComboBox();
    readonly NumericUpDown speed=new NumericUpDown(),pitch=new NumericUpDown(),buffer=new NumericUpDown();
    readonly TrackBar volume=new TrackBar();readonly CheckBox duck=new CheckBox();
    readonly Label status=new Label(),detail=new Label(),volumeLabel=new Label(),hotkeyHint=new Label();
    readonly Button read=new Button(),preview=new Button(),pause=new Button(),stop=new Button(),configure=new Button();
    readonly TextBox[] shortcutBoxes={new TextBox(),new TextBox(),new TextBox()};
    readonly NotifyIcon tray=new NotifyIcon();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly AudioDucker ducker=new AudioDucker();Preferences settings;HotkeyManager hotkeys;ReaderAction? recording;
    bool loaded,changing,ready,closing,capturing;string providerId,activeId="",failure;
    AudioPlayer player;CancellationTokenSource cancellation;
    [DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)]struct INPUT {public uint type;public UNION u;}
    [StructLayout(LayoutKind.Explicit)]struct UNION {[FieldOffset(0)]public KEYINPUT keyboard;[FieldOffset(0)]public MOUSEINPUT mouse;}
    [StructLayout(LayoutKind.Sequential)]struct KEYINPUT {public ushort key,scan;public uint flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)]struct MOUSEINPUT {public int x,y;public uint data,flags,time;public UIntPtr extra;}
    public ReaderForm(){
        data=Environment.GetEnvironmentVariable("QINGDU_DATA")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QingDu");
        Directory.CreateDirectory(data);Directory.CreateDirectory(Path.Combine(data,"logs"));settings=Preferences.Load(data);
        Text="轻读 v"+AppVersion.Value;ClientSize=new Size(900,810);MinimumSize=new Size(870,760);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(244,247,249);AutoScaleMode=AutoScaleMode.Dpi;Icon=LoadIcon(32);
        Build();LoadControls();
        tray.Icon=LoadIcon(16);tray.Text="轻读";tray.Visible=true;var menu=new ContextMenuStrip();
        menu.Items.Add("打开轻读",null,(s,e)=>ShowApp());menu.Items.Add("暂停／继续",null,(s,e)=>TogglePause());menu.Items.Add("停止",null,(s,e)=>StopRead("已停止"));menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出轻读",null,(s,e)=>Exit());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowApp();
        Shown+=(s,e)=>{hotkeys=new HotkeyManager(new WindowsHotkeyBackend(Handle));BindInitialKeys();loaded=true;SwitchProvider();timer.Start();};
        timer.Interval=800;timer.Tick+=(s,e)=>{if(player!=null&&player.IsPlaying&&!player.Paused&&duck.Checked)TryDuck();};
        FormClosing+=(s,e)=>{if(!closing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(1600,"轻读在后台运行",settings.ReadHotkey+" 朗读选区；右击托盘可退出。",ToolTipIcon.Info);}else Shutdown();};
    }
    Icon LoadIcon(int size){string path=Path.Combine(app,"assets","QingDu.ico");return File.Exists(path)?new Icon(path,size,size):Icon.ExtractAssociatedIcon(Application.ExecutablePath);}
    Label Caption(string value){return new Label{Text=value,AutoSize=true,Margin=new Padding(0,5,8,0),ForeColor=Color.FromArgb(40,62,80)};}
    Button ButtonFor(string value){return new Button{Text=value,AutoSize=true,Height=35,FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(4),Padding=new Padding(7,3,7,3)};}
    void Build(){
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,14),ColumnCount=1,RowCount=10};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
        foreach(int height in new[]{54,46,34,-1,49,56,48,139,43,54})root.RowStyles.Add(height<0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));
        var heading=new FlowLayoutPanel{Dock=DockStyle.Fill};using(var icon=LoadIcon(48))heading.Controls.Add(new PictureBox{Image=icon.ToBitmap(),Size=new Size(42,42),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,12,0)});
        heading.Controls.Add(new Label{Text="轻读",Font=new Font(Font.FontFamily,23,FontStyle.Bold),AutoSize=true,ForeColor=Color.FromArgb(23,83,112)});heading.Controls.Add(new Label{Text="v"+AppVersion.Value+" · 云端选区朗读",AutoSize=true,Margin=new Padding(14,16,0,0),ForeColor=Color.SlateGray});root.Controls.Add(heading,0,0);
        var choices=new FlowLayoutPanel{Dock=DockStyle.Fill};choices.Controls.Add(Caption("云模型"));provider.DropDownStyle=ComboBoxStyle.DropDownList;provider.Width=185;provider.AccessibleName="云模型";
        provider.Items.Add(new ProviderChoice{Id="mimo",Name="MiMo v2.5 TTS"});provider.Items.Add(new ProviderChoice{Id="fish",Name="Fish Audio"});choices.Controls.Add(provider);choices.Controls.Add(Caption("音色"));voice.Width=220;voice.DropDownWidth=440;voice.MaxDropDownItems=12;voice.DropDownStyle=ComboBoxStyle.DropDownList;voice.AccessibleName="音色";choices.Controls.Add(voice);
        preview.Text="试听";preview.Width=75;preview.Height=34;preview.Click+=(s,e)=>BeginRead("你好，这是当前音色的试听。");choices.Controls.Add(preview);configure.Text="服务设置";configure.Width=105;configure.Height=34;configure.Click+=(s,e)=>ConfigureProvider();choices.Controls.Add(configure);root.Controls.Add(choices,0,1);
        root.Controls.Add(Caption("在其他应用选中文字后按朗读快捷键，也可在下方粘贴文本。"),0,2);
        text.Multiline=true;text.ScrollBars=ScrollBars.Vertical;text.MaxLength=20000;text.Dock=DockStyle.Fill;text.BackColor=Color.White;text.Font=new Font(Font.FontFamily,12);text.AccessibleName="朗读文本";root.Controls.Add(text,0,3);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};read.Text="▶ 朗读文本";read.Width=145;read.Height=38;read.BackColor=Color.FromArgb(24,107,143);read.ForeColor=Color.White;read.FlatStyle=FlatStyle.Flat;read.Click+=(s,e)=>BeginRead(text.Text);buttons.Controls.Add(read);
        pause.Text="暂停／继续";pause.Width=130;pause.Height=38;pause.Click+=(s,e)=>TogglePause();buttons.Controls.Add(pause);stop.Text="■ 停止";stop.Width=100;stop.Height=38;stop.Click+=(s,e)=>StopRead("已停止");buttons.Controls.Add(stop);
        var paste=ButtonFor("粘贴");paste.Click+=(s,e)=>{try{if(Clipboard.ContainsText())text.Text=Clipboard.GetText();}catch{}};buttons.Controls.Add(paste);root.Controls.Add(buttons,0,4);
        var effects=new FlowLayoutPanel{Dock=DockStyle.Fill};effects.Controls.Add(Caption("语速"));speed.DecimalPlaces=2;speed.Minimum=.7m;speed.Maximum=1.5m;speed.Increment=.05m;speed.Width=82;speed.AccessibleName="语速";effects.Controls.Add(speed);
        effects.Controls.Add(Caption("  音调（半音）"));pitch.DecimalPlaces=1;pitch.Minimum=-6;pitch.Maximum=6;pitch.Increment=.5m;pitch.Width=74;pitch.AccessibleName="音调";effects.Controls.Add(pitch);effects.Controls.Add(Caption("  音量"));volume.Minimum=0;volume.Maximum=100;volume.TickFrequency=25;volume.Width=150;volume.AccessibleName="朗读音量";effects.Controls.Add(volume);volumeLabel.Width=48;volumeLabel.Margin=new Padding(0,6,0,0);effects.Controls.Add(volumeLabel);root.Controls.Add(effects,0,5);
        var streaming=new FlowLayoutPanel{Dock=DockStyle.Fill};streaming.Controls.Add(Caption("启动缓冲"));buffer.Minimum=100;buffer.Maximum=2000;buffer.Increment=50;buffer.Width=85;buffer.AccessibleName="启动缓冲毫秒";streaming.Controls.Add(buffer);streaming.Controls.Add(Caption("毫秒"));duck.Text="朗读时降低其他媒体音量";duck.AutoSize=true;duck.Margin=new Padding(24,5,0,0);streaming.Controls.Add(duck);root.Controls.Add(streaming,0,6);
        var keys=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=4};keys.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));keys.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));keys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(ReaderAction action in Enum.GetValues(typeof(ReaderAction))){int row=(int)action;keys.RowStyles.Add(new RowStyle(SizeType.Absolute,34));keys.Controls.Add(Caption(HotkeyManager.Name(action)+"快捷键"),0,row);var box=shortcutBoxes[row];box.ReadOnly=true;box.Width=175;box.AccessibleName=HotkeyManager.Name(action)+"快捷键";box.KeyDown+=RecordKey;keys.Controls.Add(box,1,row);var record=ButtonFor("修改");record.Margin=new Padding(0);record.Height=30;var captured=action;record.Click+=(s,e)=>StartCapture(captured);keys.Controls.Add(record,2,row);}
        hotkeyHint.AutoSize=false;hotkeyHint.Dock=DockStyle.Fill;hotkeyHint.Text="三个快捷键独立生效；录制时按 Esc 取消。";hotkeyHint.ForeColor=Color.SlateGray;keys.Controls.Add(hotkeyHint,0,3);keys.SetColumnSpan(hotkeyHint,3);root.Controls.Add(keys,0,7);
        status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,11,FontStyle.Bold);status.ForeColor=Color.FromArgb(23,99,131);status.AutoEllipsis=true;root.Controls.Add(status,0,8);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill};detail.AutoSize=true;detail.ForeColor=Color.SlateGray;footer.Controls.Add(detail);var about=ButtonFor("关于／许可");about.Click+=(s,e)=>MessageBox.Show(this,"轻读 v"+AppVersion.Value+"\nGPL-3.0；QingDu contributors\n包含 FFmpeg 与 Rubber Band 音频组件。\n完整许可、对应源码与构建材料见发行包 licenses 与 third_party 目录。","关于轻读");footer.Controls.Add(about);var exit=ButtonFor("退出");exit.Click+=(s,e)=>Exit();footer.Controls.Add(exit);root.Controls.Add(footer,0,9);
        provider.SelectedIndexChanged+=(s,e)=>{if(loaded&&!changing)SwitchProvider();};voice.SelectedIndexChanged+=(s,e)=>SaveControls();speed.ValueChanged+=(s,e)=>SaveControls();pitch.ValueChanged+=(s,e)=>SaveControls();buffer.ValueChanged+=(s,e)=>SaveControls();
        volume.Scroll+=(s,e)=>{volumeLabel.Text=volume.Value+"%";if(player!=null)player.Volume=volume.Value/100f;SaveControls();};duck.CheckedChanged+=(s,e)=>{if(!duck.Checked)ducker.Restore();SaveControls();};read.Enabled=false;preview.Enabled=false;pause.Enabled=false;stop.Enabled=false;
    }
    void LoadControls(){changing=true;foreach(ProviderChoice item in provider.Items)if(item.Id==settings.Provider)provider.SelectedItem=item;speed.Value=(decimal)settings.Speed;pitch.Value=(decimal)settings.Pitch;volume.Value=settings.Volume;volumeLabel.Text=volume.Value+"%";duck.Checked=settings.Duck;UpdateKeyBoxes();changing=false;}
    void SaveControls(){if(!loaded||changing)return;settings.Speed=(double)speed.Value;settings.Pitch=(double)pitch.Value;settings.Volume=volume.Value;settings.Duck=duck.Checked;if(providerId=="mimo")settings.MimoBufferMs=(int)buffer.Value;else settings.FishBufferMs=(int)buffer.Value;var v=voice.SelectedItem as Voice;if(v!=null){if(providerId=="mimo")settings.MimoVoice=v.id;else settings.FishVoice=v.id;}try{settings.Save(data);}catch{SetStatus("设置保存失败，请检查用户数据目录。");}}
    void SwitchProvider(){StopRead(null);changing=true;providerId=((ProviderChoice)provider.SelectedItem).Id;settings.Provider=providerId;buffer.Value=providerId=="mimo"?settings.MimoBufferMs:settings.FishBufferMs;RefreshProvider();changing=false;SaveControls();}
    void RefreshProvider(){
        bool previous=changing;changing=true;ready=false;string selected=providerId=="mimo"?settings.MimoVoice:settings.FishVoice;voice.Items.Clear();
        if(providerId=="mimo"){foreach(var item in MimoClient.Voices())voice.Items.Add(item);}else foreach(var item in settings.FishVoices)voice.Items.Add(item);
        int index=-1;for(int i=0;i<voice.Items.Count;i++)if(((Voice)voice.Items[i]).id==selected)index=i;if(index<0&&voice.Items.Count>0)index=providerId=="mimo"?1:0;voice.SelectedIndex=index;changing=previous;
        string secret="";try{secret=providerId=="mimo"?MimoClient.LoadKey(data):FishClient.LoadKey(data);}catch(Exception ex){SetStatus(ex.Message);}
        ready=secret.Length>0&&voice.SelectedItem is Voice;read.Enabled=preview.Enabled=ready;configure.Text=providerId=="mimo"?"MiMo 设置":"Fish 设置";
        detail.Text=providerId=="mimo"?"MiMo 按完整句组连续朗读，不添加固定停顿。\n1.00倍、音调0.0为原声；关闭窗口收起到托盘。":"Fish 整段连续朗读；免费档失败不自动切换付费。\n语速、音调和音色下次朗读生效；音量即时生效。";
        SetStatus(ready?ProviderName+" 已就绪 · "+settings.ReadHotkey+" 朗读选区":"请先打开“"+configure.Text+"”填写 Key 并选择音色。");
    }
    string ProviderName {get{return providerId=="mimo"?"MiMo":"Fish";}}
    void ConfigureProvider(){StopRead(null);using(Form form=providerId=="mimo"?(Form)new MimoSettingsForm(settings,data):new FishSettingsForm(settings,data)){if(form.ShowDialog(this)==DialogResult.OK){RefreshProvider();SaveControls();}}}
    HotkeyBinding GetBinding(ReaderAction action){return action==ReaderAction.Read?settings.ReadHotkey:action==ReaderAction.Pause?settings.PauseHotkey:settings.StopHotkey;}
    void UpdateKeyBoxes(){foreach(ReaderAction action in Enum.GetValues(typeof(ReaderAction)))shortcutBoxes[(int)action].Text=GetBinding(action).ToString();}
    void BindInitialKeys(){string errors="";foreach(ReaderAction action in Enum.GetValues(typeof(ReaderAction))){string error;if(!hotkeys.TrySet(action,GetBinding(action),out error))errors+=error+" ";}if(errors.Length>0){hotkeyHint.Text=errors;hotkeyHint.ForeColor=Color.Firebrick;}}
    void StartCapture(ReaderAction action){recording=action;UpdateKeyBoxes();shortcutBoxes[(int)action].Text="请按组合键…";shortcutBoxes[(int)action].Focus();hotkeyHint.Text="正在修改“"+HotkeyManager.Name(action)+"”；按 Esc 取消。";hotkeyHint.ForeColor=Color.SlateGray;}
    void FinishCapture(HotkeyBinding binding){if(!recording.HasValue)return;var action=recording.Value;recording=null;string error;if(hotkeys.TrySet(action,binding,out error)){if(action==ReaderAction.Read)settings.ReadHotkey=binding;else if(action==ReaderAction.Pause)settings.PauseHotkey=binding;else settings.StopHotkey=binding;hotkeyHint.Text=HotkeyManager.Name(action)+"快捷键已更新。";hotkeyHint.ForeColor=Color.SlateGray;SaveControls();}else{hotkeyHint.Text=error;hotkeyHint.ForeColor=Color.Firebrick;}UpdateKeyBoxes();}
    void RecordKey(object sender,KeyEventArgs e){if(!recording.HasValue)return;e.SuppressKeyPress=true;e.Handled=true;if(e.KeyCode==Keys.Escape){recording=null;UpdateKeyBoxes();hotkeyHint.Text="已取消修改。";return;}if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.Menu||e.KeyCode==Keys.ShiftKey)return;FinishCapture(new HotkeyBinding((e.Control?2:0)|(e.Alt?1:0)|(e.Shift?4:0),(int)e.KeyCode));}
    protected override void WndProc(ref Message m){if(m.Msg==0x312&&hotkeys!=null){ReaderAction action;if(hotkeys.Resolve(m.WParam.ToInt32(),out action)){if(recording.HasValue)FinishCapture(hotkeys.Binding(action));else if(action==ReaderAction.Read)CaptureSelection();else if(action==ReaderAction.Pause)TogglePause();else StopRead("已停止");}}else if(m.Msg==0x8001)ShowApp();else if(m.Msg==0x8002)Exit();base.WndProc(ref m);}
    async void CaptureSelection(){
        if(capturing)return;if(!ready){SetStatus("请先配置当前云模型的 Key 和音色。");return;}capturing=true;
        try{
            if(GetForegroundWindow()==Handle){BeginRead(String.IsNullOrWhiteSpace(text.SelectedText)?text.Text:text.SelectedText);return;}
            for(int i=0;i<80&&(GetAsyncKeyState(0x11)<0||GetAsyncKeyState(0x12)<0||GetAsyncKeyState(0x10)<0);i++)await Task.Delay(10);
            if(GetAsyncKeyState(0x11)<0||GetAsyncKeyState(0x12)<0){SetStatus("请松开快捷键后再试。");return;}
            IDataObject backup=null;try{backup=Clipboard.GetDataObject();}catch{}uint before=GetClipboardSequenceNumber();
            var inputs=new[]{new INPUT{type=1,u=new UNION{keyboard=new KEYINPUT{key=0x11}}},new INPUT{type=1,u=new UNION{keyboard=new KEYINPUT{key=0x43}}},new INPUT{type=1,u=new UNION{keyboard=new KEYINPUT{key=0x43,flags=2}}},new INPUT{type=1,u=new UNION{keyboard=new KEYINPUT{key=0x11,flags=2}}}};
            if(SendInput(4,inputs,Marshal.SizeOf(typeof(INPUT)))!=4){SetStatus("无法复制此应用的选区，请手动粘贴。");return;}
            string value=null;uint copied=before;for(int i=0;i<70;i++){await Task.Delay(10);copied=GetClipboardSequenceNumber();if(copied!=before){try{if(Clipboard.ContainsText())value=Clipboard.GetText();}catch{}if(value!=null)break;}}
            if(copied!=before&&backup!=null&&GetClipboardSequenceNumber()==copied){try{Clipboard.SetDataObject(backup,true);}catch{Log("Clipboard restoration unavailable");}}
            if(String.IsNullOrWhiteSpace(value)){SetStatus("未获取选中文字，请选中文本后重试。");return;}text.Text=value;BeginRead(value);
        }catch{SetStatus("获取选区失败，请手动粘贴。");}finally{capturing=false;}
    }
    void BeginRead(string value){
        if(!ready){SetStatus("请先配置当前云模型的 Key 和音色。");return;}if(String.IsNullOrWhiteSpace(value)){SetStatus("请先选中或粘贴文本。");return;}
        bool mimo=providerId=="mimo";if(value.Length>(mimo?3000:20000)){SetStatus(mimo?"MiMo 单次最多3000字，请分次朗读。":"Fish 单次最多20000字，请分次朗读。");return;}
        string secret;try{secret=mimo?MimoClient.LoadKey(data):FishClient.LoadKey(data);}catch(Exception ex){SetStatus(ex.Message);return;}if(secret.Length==0){RefreshProvider();return;}
        string reference=((Voice)voice.SelectedItem).id,model=settings.FishModel,style=settings.MimoStyle,name=ProviderName;double rate=(double)speed.Value,tone=(double)pitch.Value;
        StopRead(null);failure=null;string id=Guid.NewGuid().ToString("N");activeId=id;cancellation=new CancellationTokenSource();var token=cancellation.Token;var watch=Stopwatch.StartNew();
        var current=new AudioPlayer(.02,(double)buffer.Value/1000);player=current;current.Volume=volume.Value/100f;pause.Enabled=stop.Enabled=true;SetStatus(name+" 正在生成语音…");
        current.Started=()=>UI(()=>{if(activeId!=id||current.Paused)return;if(duck.Checked)TryDuck();Log(name+" first_playback_ms="+watch.ElapsedMilliseconds);SetStatus(name+" 正在朗读 · 起播 "+watch.Elapsed.TotalSeconds.ToString("F1")+" 秒");});
        current.Completed=()=>UI(()=>{if(activeId!=id)return;ducker.Restore();Log(name+" completed; underruns="+current.Underruns+"; missing_s="+current.MissingSeconds.ToString("F3"));pause.Enabled=stop.Enabled=false;pause.Text="暂停／继续";SetStatus(failure??(name+" 朗读完成"+(current.Underruns>0?" · 可提高缓冲减少等待":"")));activeId="";player=null;current.Dispose();});
        current.Failed=message=>UI(()=>{if(activeId==id)StopRead(message);});Log(name+" read_started; characters="+value.Length);
        Task.Run(()=>{try{
            if(mimo)MimoClient.Speak(secret,reference,value,style,rate,tone,Path.Combine(app,"bin","ffmpeg.exe"),token,pcm=>current.Enqueue(pcm,token),null,s=>Log("MiMo segment="+s.Segment+"/"+s.Segments+"; chars="+s.Characters+"; audio_s="+(s.AudioBytes/48000.0).ToString("F2")+"; finish="+s.FinishReason+"; done="+s.Done+"; output_tokens="+s.CompletionTokens),(index,count)=>{if(count>1)UI(()=>{if(activeId==id&&!current.Paused)SetStatus("MiMo 流式朗读 · 准备第 "+index+" / "+count+" 段");});});
            else FishClient.Speak(secret,model,reference,value,rate,tone,Path.Combine(app,"bin","ffmpeg.exe"),token,pcm=>current.Enqueue(pcm,token));
        }catch(Exception ex){if(!token.IsCancellationRequested){string message=mimo?MimoClient.Error(ex):FishClient.Error(ex);UI(()=>{if(activeId==id){failure=message;SetStatus(message);}});Log(message);}}finally{secret=null;current.Finish();}});
    }
    void StopRead(string message){activeId="";if(cancellation!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;}var old=player;player=null;if(old!=null)old.Dispose();ducker.Restore();pause.Enabled=stop.Enabled=false;pause.Text="暂停／继续";if(message!=null)SetStatus(message);}
    void TogglePause(){if(player==null)return;player.Paused=!player.Paused;if(player.Paused){ducker.Restore();pause.Text="继续朗读";SetStatus("已暂停");}else{if(duck.Checked)TryDuck();pause.Text="暂停／继续";SetStatus("正在朗读");}}
    void TryDuck(){try{ducker.Duck();}catch{Log("Ducking unavailable");}}
    void Log(string message){try{File.AppendAllText(Path.Combine(data,"logs","app.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8);}catch{}}
    void UI(Action action){if(IsDisposed||Disposing)return;try{BeginInvoke(action);}catch{}}
    void SetStatus(string message){status.Text=message;string value="轻读 · "+message;tray.Text=value.Substring(0,Math.Min(62,value.Length));}
    void ShowApp(){Show();WindowState=FormWindowState.Normal;Activate();}
    void Exit(){closing=true;Close();}
    void Shutdown(){timer.Stop();SaveControls();StopRead(null);if(hotkeys!=null)hotkeys.Dispose();tray.Visible=false;tray.Dispose();ducker.Dispose();}
}
}
