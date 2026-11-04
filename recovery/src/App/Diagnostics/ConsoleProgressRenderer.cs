using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace NewModCompiler.Diagnostics
{
    public sealed class ConsoleProgressRenderer : IDisposable
    {
        readonly object gate=new object();
        readonly BuildProgressState state;
        readonly TextWriter writer;
        readonly bool interactive;
        readonly Func<int> width;
        readonly Timer timer;
        bool disposed, outputFailed;
        int lineCells;
        long lastDrawMilliseconds=-1000;
        public ConsoleProgressRenderer(BuildProgressState state)
            : this(state,Console.Error,!Console.IsErrorRedirected,ConsoleWidth,true) { }
        internal ConsoleProgressRenderer(BuildProgressState state,TextWriter writer,bool interactive,Func<int> width,bool startTimer=true)
        {
            this.state=state; this.writer=writer; this.interactive=interactive; this.width=width;
            state.Boundary+=OnBoundary;
            if (interactive && startTimer) timer=new Timer(_=>Refresh(),null,1000,1000);
        }
        static int ConsoleWidth()
        {
            try { return Console.BufferWidth; }
            catch (IOException) { return 120; }
            catch (InvalidOperationException) { return 120; }
        }
        // Called by the low-frequency timer (or by deterministic terminal tests).
        internal void Refresh()
        {
            lock(gate) {
                if (disposed || outputFailed || !interactive) return;
                BuildProgressSnapshot value=state.Snapshot();
                if (!value.Finished && value.Stage!=null && value.ElapsedMilliseconds-lastDrawMilliseconds>=1000)
                    Output(()=>Draw(Format(value,"进行",true)));
            }
        }
        void OnBoundary(BuildProgressSnapshot value,string boundary,string status)
        {
            lock(gate) {
                if (disposed || outputFailed) return;
                Output(()=> {
                    if (boundary=="finished") {
                        ClearLine();
                        writer.WriteLine("[{0}] 编译{1} | 总耗时 {2}",Label(status),Result(status),Seconds(value.ElapsedMilliseconds));
                    } else if (boundary=="stage-completed" || boundary=="stream-completed") {
                        ClearLine(); writer.WriteLine(Fit(Format(value,Label(status),boundary=="stream-completed")));
                    } else if (interactive) {
                        Draw(Format(value,"进行",true));
                    } else if (boundary=="stage-started" || boundary=="stream-started") {
                        writer.WriteLine(Format(value,"开始",true));
                    }
                    writer.Flush();
                });
            }
        }
        public void WriteLine(string message)
        {
            lock(gate) {
                if (disposed || outputFailed) return;
                Output(()=> {
                    // Diagnostics are immediate; the existing timer restores
                    // the progress row once per second, including warning bursts.
                    ClearLine(); writer.WriteLine(message);
                    writer.Flush();
                });
            }
        }
        void Output(Action action)
        {
            try { action(); }
            catch (IOException) { outputFailed=true; }
            catch (ObjectDisposedException) { outputFailed=true; }
            catch (UnauthorizedAccessException) { outputFailed=true; }
        }
        void Draw(string text)
        {
            text=Fit(text);
            int cells=Cells(text);
            writer.Write('\r'); writer.Write(text);
            int previousCells=Math.Min(lineCells,Math.Max(1,width()-1));
            if (previousCells>cells) writer.Write(new string(' ',previousCells-cells));
            lineCells=cells;
            writer.Flush();
            // Timestamp after the write: queued callbacks following a slow
            // terminal must still wait a full interval before another draw.
            lastDrawMilliseconds=state.Snapshot().ElapsedMilliseconds;
        }
        void ClearLine()
        {
            if (!interactive || lineCells==0) return;
            writer.Write("\r"+new string(' ',Math.Min(lineCells,Math.Max(1,width()-1)))+"\r"); lineCells=0;
        }
        string Fit(string text)
        {
            if (!interactive) return text;
            int available=Math.Max(1,width()-1), cells=0;
            for(int index=0;index<text.Length;index++) {
                int count=CharCells(text[index]);
                if (cells+count>available) return text.Substring(0,index);
                cells+=count;
            }
            return text;
        }
        static int Cells(string value)
        {
            int count=0;
            foreach(char item in value) count+=CharCells(item);
            return count;
        }
        static int CharCells(char value)
        {
            if (char.IsLowSurrogate(value) || CharUnicodeInfo.GetUnicodeCategory(value)==UnicodeCategory.NonSpacingMark) return 0;
            return value>='\u1100' && (value<='\u115f' || value=='\u2329' || value=='\u232a' ||
                (value>='\u2e80' && value<='\ua4cf') || (value>='\uac00' && value<='\ud7a3') ||
                (value>='\uf900' && value<='\ufaff') || (value>='\ufe10' && value<='\ufe6f') ||
                (value>='\uff01' && value<='\uff60') || (value>='\uffe0' && value<='\uffe6') || char.IsHighSurrogate(value)) ? 2 : 1;
        }
        static string Clean(string value)
        {
            if (value==null) return "";
            var result=new StringBuilder(value.Length);
            foreach(char item in value) result.Append(char.IsControl(item) ? ' ' : item);
            return result.ToString();
        }
        string Format(BuildProgressSnapshot value,string label,bool stream)
        {
            string prefix="["+label+"] ";
            string time=Seconds(stream && value.Stream!=null ? value.StreamElapsedMilliseconds : value.StageElapsedMilliseconds);
            if (stream && value.Stream!=null) {
                string head=prefix+value.StreamsCompleted.ToString(CultureInfo.InvariantCulture)+"/"+
                    value.StreamsTotal.ToString(CultureInfo.InvariantCulture)+" ";
                string tail=" | 完成 "+value.AssetsCompleted.ToString("N0",CultureInfo.InvariantCulture)+
                    " | XML "+value.XmlParsed.ToString("N0",CultureInfo.InvariantCulture)+
                    " | 复用文档 "+value.DocumentsReused.ToString("N0",CultureInfo.InvariantCulture)+" | "+time;
                string phase=value.Phase==null ? "" : " | "+PhaseName(value.Phase);
                string name=Clean(value.Stream);
                if (interactive) {
                    int available=Math.Max(1,width()-1);
                    int nameBudget=available-Cells(head+phase+tail);
                    // Keep true work counts/time visible when a long path or a
                    // narrow terminal leaves little room for a stream name.
                    if (nameBudget<8) { phase="";nameBudget=available-Cells(head+tail); }
                    name=Shorten(name,Math.Max(0,nameBudget));
                }
                return head+name+phase+tail;
            }
            string stage=StageName(value.Stage);
            if (stream && value.Phase!=null) stage+=" | "+PhaseName(value.Phase);
            return prefix+stage+" | "+time;
        }
        static string Shorten(string value,int maximumCells)
        {
            if (Cells(value)<=maximumCells) return value;
            if(maximumCells==0) return "";
            int count=0;
            for(int index=0;index<value.Length;index++) {
                int next=CharCells(value[index]);
                if(count+next>maximumCells-1) return value.Substring(0,index)+"…";
                count+=next;
            }
            return value;
        }
        static string StageName(string id)
        {
            switch(id) { case "plan":return "规划编译";case "compile":return "编译数据流";case "package":return "打包";case "publish":return "发布";default:return Clean(id); }
        }
        static string PhaseName(string id)
        {
            switch(id) { case "initialize":return "准备编译环境";case "cache-load":return "加载缓存";case "process":return "XML/资产";case "cache-save":return "保存缓存";case "link":return "链接输出";default:return Clean(id); }
        }
        static string Label(string status) { return status=="succeeded" ? "完成" : status=="cancelled" ? "取消" : "失败"; }
        static string Result(string status) { return status=="succeeded" ? "成功" : status=="cancelled" ? "取消" : "失败"; }
        static string Seconds(long milliseconds) { return (milliseconds/1000.0).ToString("F1",CultureInfo.InvariantCulture)+"s"; }
        public void Dispose()
        {
            state.Boundary-=OnBoundary;
            lock(gate) {
                if(disposed) return;
                disposed=true;
                if(timer!=null) timer.Dispose();
                if(!outputFailed) Output(()=> { ClearLine(); writer.Flush(); });
            }
        }
    }
}
