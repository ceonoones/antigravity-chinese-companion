using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AntigravityChinese {
  static class Program {
    internal const string World = "AntigravityChineseCompanion_v1";
    internal static JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
    internal static string EventName = "Local\\AntigravityChineseCompanion_Stop_" + Environment.UserName;
    internal static void Trace(string message) {
      var target=Environment.GetEnvironmentVariable("AGZH_TRACE");
      if(!string.IsNullOrEmpty(target))try{File.AppendAllText(target,DateTime.Now.ToString("HH:mm:ss ")+message+Environment.NewLine);}catch{}
    }
    [STAThread] static void Main(string[] args) {
      if (Array.IndexOf(args, "--stop") >= 0) {
        try { using (var e = EventWaitHandle.OpenExisting(EventName)) e.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
        return;
      }
      bool created;
      using (var mutex = new Mutex(true, "Local\\AntigravityChineseCompanion_Run_" + Environment.UserName, out created)) {
        if (!created) { MessageBox.Show("中文伴侣已在运行。请在系统托盘中查看，右键可恢复英文并退出。", "Antigravity 中文伴侣"); return; }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.Run(new Companion()); }
        catch (Exception e) { MessageBox.Show("启动失败：" + e.Message, "Antigravity 中文伴侣"); }
        finally { mutex.ReleaseMutex(); }
      }
    }
    internal static string Resource(string name) {
      using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
      using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
    }
    internal static Dictionary<string,object> Obj(object value) { return value as Dictionary<string,object>; }
    internal static object Val(object value, string key) { var d=Obj(value);return d!=null&&d.ContainsKey(key)?d[key]:null; }
  }

  sealed class Cdp : IDisposable {
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly Dictionary<int,string> worlds = new Dictionary<int,string>();
    int sequence;
    internal int Port;
    internal string Id, ScriptId;
    internal Cdp(int port, string id) { Port=port; Id=id; }
    internal async Task Connect(string url) {
      using (var timeout = new CancellationTokenSource(3500)) await socket.ConnectAsync(new Uri(url),timeout.Token).ConfigureAwait(false);
      await Call("Runtime.enable",new {}).ConfigureAwait(false);
    }
    internal async Task<Dictionary<string,object>> Call(string method, object parameters) {
      int id=++sequence;
      var bytes=Encoding.UTF8.GetBytes(Program.Json.Serialize(new { id=id, method=method, @params=parameters }));
      using (var timeout=new CancellationTokenSource(3500)) {
        await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,timeout.Token).ConfigureAwait(false);
        while(true) {
          using(var data=new MemoryStream()) {
            WebSocketReceiveResult received;
            var buffer=new byte[16384];
            do {
              received=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token).ConfigureAwait(false);
              if(received.MessageType==WebSocketMessageType.Close) throw new IOException("客户端页面已关闭");
              data.Write(buffer,0,received.Count);
              if(data.Length>4*1024*1024) throw new IOException("调试接口响应过大");
            } while(!received.EndOfMessage);
            var message=Program.Json.DeserializeObject(Encoding.UTF8.GetString(data.ToArray()));
            string eventName=Convert.ToString(Program.Val(message,"method"));
            var eventParams=Program.Val(message,"params");
            if(eventName=="Runtime.executionContextsCleared")worlds.Clear();
            if(eventName=="Runtime.executionContextDestroyed")worlds.Remove(Convert.ToInt32(Program.Val(eventParams,"executionContextId")??0));
            if(eventName=="Runtime.executionContextCreated") {
              var context=Program.Val(eventParams,"context");
              if(Convert.ToString(Program.Val(context,"name"))==Program.World) {
                worlds[Convert.ToInt32(Program.Val(context,"id"))]=Convert.ToString(Program.Val(Program.Val(context,"auxData"),"frameId"));
              }
            }
            if(Convert.ToInt32(Program.Val(message,"id")??0)!=id) continue;
            if(Program.Val(message,"error")!=null) throw new IOException("调试接口暂不可用");
            return Program.Obj(Program.Val(message,"result"));
          }
        }
      }
    }
    internal async Task<int> WorldContext() {
      var tree=await Call("Page.getFrameTree",new {}).ConfigureAwait(false);
      var frame=Program.Val(Program.Val(tree,"frameTree"),"frame");
      string frameId=Convert.ToString(Program.Val(frame,"id"));
      foreach(var entry in worlds)if(entry.Value==frameId)return entry.Key;
      var result=await Call("Page.createIsolatedWorld",new {frameId=Program.Val(frame,"id"),worldName=Program.World}).ConfigureAwait(false);
      int contextId=Convert.ToInt32(Program.Val(result,"executionContextId"));worlds[contextId]=frameId;return contextId;
    }
    internal async Task<object> Evaluate(string expression) {
      int context=await WorldContext().ConfigureAwait(false);
      var result=await Call("Runtime.evaluate",new {expression=expression,contextId=context,returnByValue=true}).ConfigureAwait(false);
      if(Program.Val(result,"exceptionDetails")!=null) throw new IOException("汉化脚本执行失败");
      return Program.Val(Program.Val(result,"result"),"value");
    }
    internal async Task Restore() {
      if(ScriptId!=null) { await Call("Page.removeScriptToEvaluateOnNewDocument",new {identifier=ScriptId}).ConfigureAwait(false); ScriptId=null; }
      await RestoreWorlds().ConfigureAwait(false);
    }
    internal async Task RestoreWorlds() {
      // A DOM event crosses isolated-world boundaries, including disconnected sessions.
      await Evaluate("document.dispatchEvent(new Event('ag-chinese-restore-v1'))").ConfigureAwait(false);
      foreach(var contextId in new List<int>(worlds.Keys)) {
        try {
          await Call("Runtime.evaluate",new {expression="globalThis.__agChineseUI ? globalThis.__agChineseUI.restore() : null",contextId=contextId,returnByValue=true}).ConfigureAwait(false);
        } catch { }
      }
    }
    public void Dispose() { socket.Abort(); socket.Dispose(); }
  }

  sealed class Companion : ApplicationContext {
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem status, toggle;
    readonly System.Windows.Forms.Timer timer;
    readonly EventWaitHandle stopEvent;
    readonly Dictionary<string,Cdp> pages=new Dictionary<string,Cdp>();
    readonly string source;
    bool enabled=true, busy, stopping;
    int ticks;
    Task cycle=Task.FromResult(0);
    internal Companion() {
      var dictionary=Program.Resource("dictionary.json");
      var custom=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"汉化词典.json");
      if(File.Exists(custom)) dictionary=File.ReadAllText(custom,Encoding.UTF8);
      var parsed=Program.Json.Deserialize<Dictionary<string,string>>(dictionary);
      source=Program.Resource("ui-translator.js").Replace("__AG_CHINESE_DICTIONARY__",Program.Json.Serialize(parsed));
      stopEvent=new EventWaitHandle(false,EventResetMode.AutoReset,Program.EventName);
      var menu=new ContextMenuStrip();
      status=new ToolStripMenuItem("正在等待 Antigravity…") {Enabled=false};
      toggle=new ToolStripMenuItem("暂停汉化并恢复英文");
      toggle.Click+=async (s,e)=>await Toggle();
      menu.Items.Add(status);menu.Items.Add(new ToolStripSeparator());menu.Items.Add(toggle);
      var help=new ToolStripMenuItem("打开使用说明");
      help.Click+=(s,e)=>{var p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.md");if(File.Exists(p))Process.Start(new ProcessStartInfo(p){UseShellExecute=true});};
      menu.Items.Add(help);
      var exit=new ToolStripMenuItem("恢复英文并退出");exit.Click+=async (s,e)=>await Shutdown();menu.Items.Add(exit);
      tray=new NotifyIcon { Icon=SystemIcons.Application,Text="Antigravity 中文伴侣",ContextMenuStrip=menu,Visible=true };
      tray.DoubleClick+=(s,e)=>tray.ShowBalloonTip(3000,"Antigravity 中文伴侣",status.Text,ToolTipIcon.Info);
      timer=new System.Windows.Forms.Timer { Interval=500 };
      timer.Tick+=async(s,e)=> {
        if(stopping)return;
        if(stopEvent.WaitOne(0)){await Shutdown();return;}
        if(!enabled||busy||ticks++%4!=0)return;
        busy=true;
        try { cycle=Pump();await cycle; }
        catch (Exception err) { Program.Trace(err.ToString());status.Text="连接暂不可用，正在重试…"; }
        finally { busy=false; }
      };
      timer.Start();
      tray.ShowBalloonTip(4000,"Antigravity 中文伴侣","汉化伴侣已启动。只处理界面显示；右键此托盘图标可恢复英文。",ToolTipIcon.Info);
      Application.ApplicationExit+=(s,e)=>{tray.Visible=false;};
    }
    async Task<string> LocalGet(string url) {
      var request=(HttpWebRequest)WebRequest.Create(url);request.Proxy=null;request.Timeout=2500;request.ReadWriteTimeout=2500;
      using(var timeout=new CancellationTokenSource(3000))
      using(timeout.Token.Register(()=>request.Abort()))
      using(var response=await request.GetResponseAsync().ConfigureAwait(false))
      using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
    async Task Pump() {
      var activePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Antigravity","DevToolsActivePort");
      if(!File.Exists(activePath)){status.Text="等待客户端启动（无需重启）…";return;}
      int port;
      if(!int.TryParse(File.ReadAllLines(activePath)[0],out port)||port<1||port>65535) {status.Text="客户端接口端口无效";return;}
      object[] targets=null;
      try { targets=Program.Json.DeserializeObject(await LocalGet("http://127.0.0.1:"+port+"/json/list")) as object[]; }
      catch (Exception e) {Program.Trace(e.ToString());}
      if(targets==null){await ClearConnections();status.Text="等待客户端本地接口…";return;}
      var alive=new HashSet<string>();int ready=0,count=0;
      if(targets!=null)foreach(var target in targets) {
        string url=Convert.ToString(Program.Val(target,"url")),id=Convert.ToString(Program.Val(target,"id"));
        if(Convert.ToString(Program.Val(target,"type"))!="page"||!Regex.IsMatch(url,@"^https://127\.0\.0\.1:\d+/"))continue;
        string ws=Convert.ToString(Program.Val(target,"webSocketDebuggerUrl"));Uri uri;
        if(!Uri.TryCreate(ws,UriKind.Absolute,out uri)||uri.Scheme!="ws"||uri.Host!="127.0.0.1"||uri.Port!=port||!uri.AbsolutePath.StartsWith("/devtools/page/"))continue;
        string key=port+":"+id;alive.Add(key);Cdp connection;bool failed=false;
        try {
          if(!pages.TryGetValue(key,out connection)) {
            connection=new Cdp(port,id);pages.Add(key,connection);
            await connection.Connect(ws);
            await connection.Call("Page.enable",new {});
            await connection.RestoreWorlds();
            var result=await connection.Call("Page.addScriptToEvaluateOnNewDocument",new {source=source,worldName=Program.World});
            connection.ScriptId=Convert.ToString(Program.Val(result,"identifier"));
          }
          var resultValue=await connection.Evaluate(source);
          if(Convert.ToBoolean(Program.Val(resultValue,"ready")??false)){ready++;count+=Convert.ToInt32(Program.Val(resultValue,"translated")??0);}
        } catch (Exception e) {Program.Trace(e.ToString());failed=true;}
        if(failed){
          if(pages.TryGetValue(key,out connection)){try{await connection.Restore();}catch{}connection.Dispose();pages.Remove(key);}
        }
      }
      foreach(var key in new List<string>(pages.Keys))if(!alive.Contains(key)){pages[key].Dispose();pages.Remove(key);}
      status.Text=ready>0?"已连接 "+ready+" 个界面，已汉化 "+count+" 处文字":"等待主界面就绪…";
      Program.Trace(status.Text);
      tray.Text=ready>0?"Antigravity 中文伴侣：汉化中":"Antigravity 中文伴侣：等待客户端";
    }
    async Task ClearConnections() {
      foreach(var p in pages.Values){try{await p.Restore();}catch{}p.Dispose();}pages.Clear();
    }
    async Task Toggle() {
      if(stopping)return;toggle.Enabled=false;enabled=false;
      try {await cycle;await ClearConnections();}
      finally {toggle.Enabled=true;}
      if(toggle.Text=="暂停汉化并恢复英文") {
        toggle.Text="启用中文汉化";status.Text="已暂停，界面已恢复英文";tray.Text="Antigravity 中文伴侣：已暂停";
      } else {enabled=true;ticks=0;toggle.Text="暂停汉化并恢复英文";status.Text="正在连接…";}
    }
    async Task Shutdown() {
      if(stopping)return;stopping=true;enabled=false;timer.Stop();toggle.Enabled=false;status.Text="正在恢复英文…";
      try {await cycle;await ClearConnections();}catch{}
      timer.Dispose();stopEvent.Dispose();tray.Visible=false;tray.Dispose();ExitThread();
    }
  }
}
