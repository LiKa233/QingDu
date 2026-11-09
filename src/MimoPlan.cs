using System;
using System.Collections.Generic;
using System.Text;

namespace QingDu {
public static class MimoPlan {
    // Preserve every character in order. Only move request boundaries; never invent pauses or overlap text.
    public static List<string> Split(string text,int target=140,int maximum=220){
        if(target<20||maximum<target)throw new ArgumentOutOfRangeException("target");
        text=(text??"").Trim();var sentences=new List<string>();int start=0;
        for(int i=0;i<text.Length;i++){
            char c=text[i];bool end=c=='。'||c=='！'||c=='？'||c=='!'||c=='?'||(c=='.'&&(i+1==text.Length||Char.IsWhiteSpace(text[i+1])))||(c=='\n'&&i+1<text.Length&&text[i+1]=='\n');
            if(!end)continue;int next=i+1;while(next<text.Length&&("”’\"'）)]】".IndexOf(text[next])>=0||Char.IsWhiteSpace(text[next])))next++;
            sentences.Add(text.Substring(start,next-start));start=next;i=next-1;
        }
        if(start<text.Length)sentences.Add(text.Substring(start));
        var units=new List<string>();foreach(string sentence in sentences){int offset=0;while(sentence.Length-offset>maximum){int cut=-1;for(int i=offset+Math.Min(target,sentence.Length-offset)-1;i>offset+target/2;i--)if("，,；;、 \t".IndexOf(sentence[i])>=0){cut=i+1;break;}if(cut<0)cut=offset+maximum;if(cut<sentence.Length&&Char.IsHighSurrogate(sentence[cut-1])&&Char.IsLowSurrogate(sentence[cut]))cut--;units.Add(sentence.Substring(offset,cut-offset));offset=cut;}if(offset<sentence.Length)units.Add(sentence.Substring(offset));}
        var result=new List<string>();var current=new StringBuilder();foreach(string unit in units){if(current.Length>0&&(current.Length+unit.Length>maximum||(current.Length>=target/2&&current.Length+unit.Length>target))){result.Add(current.ToString());current.Length=0;}current.Append(unit);}if(current.Length>0)result.Add(current.ToString());return result;
    }
}
public sealed class MimoStreamStats {
    public int Segment,Segments,Characters,AudioChunks,DuplicateAudioChunks;
    public long AudioBytes,ElapsedMs,FirstAudioMs=-1;
    public string FinishReason="missing";public bool Done;public long CompletionTokens=-1,PromptTokens=-1;
}
}
