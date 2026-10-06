using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Autotyper;
static class Native {
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int MT_Init(IntPtr window);
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int MT_Type(int code,int wpm,double variability);
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void MT_Stop();
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void MT_ResetStop();
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void MT_Close();
    [DllImport("typing_engine.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr MT_Error();
    internal static string Error => Marshal.PtrToStringUTF8(MT_Error()) ?? "Ошибка движка";
}
static class Program {
    [STAThread] static void Main() {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
    }
}
sealed class MainWindow:Form {
    readonly WebView2 panel=new(){Dock=DockStyle.Top,Height=220};
    readonly WebView2 site=new(){Dock=DockStyle.Fill};
    readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InterceptionAutotyper");
    CancellationTokenSource? cancellation;
    bool ready,closeAfterStop;
    int count;
    string status="Загрузка интерфейса…",reason="Остановлено";
    string snapshotScript="";
    readonly Stopwatch stopwatch=new();
    readonly System.Windows.Forms.Timer stopTimer=new(){Interval=15};
    readonly List<object> timings=new();
    public MainWindow(){
        Text="Автотайпер 0.3 · Interception · RU/EN";Width=1280;Height=900;MinimumSize=new Size(850,680);
        Controls.Add(site);Controls.Add(panel);
        Shown+=async(_,_)=>await Initialize();
        stopTimer.Tick+=(_,_)=>{
            if(cancellation!=null && ((Native.GetAsyncKeyState(0x1B)&0x8000)!=0 || (Native.GetAsyncKeyState(0x78)&0x8000)!=0))
                Stop("Остановлено клавишей Esc / F9");
        };
        stopTimer.Start();
        FormClosed+=(_,_)=>stopTimer.Dispose();
        Deactivate+=(_,_)=>Stop("Окно потеряло фокус");
        FormClosing+=(_,e)=>{if(cancellation!=null){e.Cancel=true;closeAfterStop=true;Stop("Закрытие приложения");}};
    }
    async Task Initialize(){
        try{
            Directory.CreateDirectory(dataDir);
            snapshotScript=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"snapshot.js"));
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,"browser"));
            await panel.EnsureCoreWebView2Async(env);await site.EnsureCoreWebView2Async(env);
            panel.CoreWebView2.SetVirtualHostNameToFolderMapping("ui.autotyper.local",Path.Combine(AppContext.BaseDirectory,"ui"),CoreWebView2HostResourceAccessKind.DenyCors);
            panel.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            panel.CoreWebView2.Settings.AreDevToolsEnabled=false;
            panel.CoreWebView2.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://ui.autotyper.local/",StringComparison.Ordinal))e.Cancel=true;};
            panel.CoreWebView2.NewWindowRequested+=(_,e)=>e.Handled=true;
            panel.CoreWebView2.WebMessageReceived+=async(_,e)=>{
                if(!Uri.TryCreate(e.Source,UriKind.Absolute,out var origin)||origin.Host!="ui.autotyper.local"||origin.Scheme!="https")return;
                try{
                    using var message=JsonDocument.Parse(e.WebMessageAsJson);
                    var root=message.RootElement;var action=root.GetProperty("action").GetString();
                    if(action=="start")await Run(root.GetProperty("wpm").GetInt32(),root.GetProperty("variability").GetDouble());
                    else if(action=="stop")Stop("Остановлено пользователем");
                    else if(action=="reload"){Stop("Перезагрузка сайта");site.Reload();}
                    else if(action=="logs")Process.Start(new ProcessStartInfo("explorer.exe",dataDir){UseShellExecute=true});
                    else if(action=="ready")Report(status);
                }catch(Exception ex){Report(ex.Message);}
            };
            site.CoreWebView2.Settings.IsWebMessageEnabled=false;
            site.CoreWebView2.Settings.AreHostObjectsAllowed=false;
            site.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;Report("Всплывающее окно заблокировано. Встроенный браузер предназначен для теста без внешней авторизации.");};
            site.CoreWebView2.NavigationStarting+=(_,e)=>{
                Stop("Навигация: ввод остановлен");
                if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="monkeytype.com")e.Cancel=true;
            };
            site.CoreWebView2.ProcessFailed+=(_,_)=>Stop("Процесс страницы завершился с ошибкой");
            panel.CoreWebView2.Navigate("https://ui.autotyper.local/index.html");
            site.CoreWebView2.Navigate("https://monkeytype.com");
            ready=true;Report("Выберите английский или русский тест. Нажмите «Запустить», затем F8. Esc / F9 останавливает.");
        }catch(Exception ex){MessageBox.Show("Не удалось запустить приложение. Проверьте WebView2 Runtime.\n\n"+ex.Message,"Ошибка запуска");Close();}
    }
    void Report(string text){
        status=text;
        if(panel.CoreWebView2==null||IsDisposed)return;
        double actual=stopwatch.Elapsed.TotalMinutes>0 ? count/5.0/stopwatch.Elapsed.TotalMinutes : 0;
        panel.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{status=text,running=cancellation!=null,count,actual=Math.Round(actual,1)}));
    }
    void Stop(string text){
        if(cancellation==null)return;
        reason=text;cancellation.Cancel();
        try{Native.MT_Stop();}catch(Exception ex){Report(ex.Message);}
    }
    async Task<JsonElement> Snapshot(CancellationToken token){
        token.ThrowIfCancellationRequested();
        string json=await site.CoreWebView2.ExecuteScriptAsync(snapshotScript).WaitAsync(TimeSpan.FromSeconds(3),token);
        using var doc=JsonDocument.Parse(json);return doc.RootElement.Clone();
    }
    static string State(JsonElement s)=>s.ValueKind==JsonValueKind.Object&&s.TryGetProperty("state",out var v)?v.GetString()??"error":"error";
    static string Error(JsonElement s)=>s.ValueKind==JsonValueKind.Object&&s.TryGetProperty("error",out var v)?v.GetString()??"Ошибка страницы":"Поле ввода потеряло фокус или недоступно";
    async Task Run(int wpm,double variability){
        if(!ready||cancellation!=null)return;
        if(wpm<10||wpm>300||!double.IsFinite(variability)||variability<0.03||variability>0.45){Report("Проверьте скорость и вариативность");return;}
        using var cts=new CancellationTokenSource();cancellation=cts;
        var token=cts.Token;count=0;timings.Clear();stopwatch.Reset();
        try{
            Native.MT_ResetStop();
            Report("Нажмите и отпустите F8 в течение 20 секунд. Раскладка EN-US или RU по языку теста; Caps Lock выключен.");
            site.Focus();var window=Handle;
            if(await Task.Run(()=>Native.MT_Init(window))!=1)throw new Exception(Native.Error);
            token.ThrowIfCancellationRequested();
            site.Focus();
            await site.CoreWebView2.ExecuteScriptAsync("document.querySelector('#wordsInput')?.focus()").WaitAsync(TimeSpan.FromSeconds(3),token);
            await Task.Delay(100,token);
            stopwatch.Start();Report("Идёт ввод. Esc / F9: остановка. Не печатайте одновременно вручную.");
            var before=await Snapshot(token);
            while(true){
                token.ThrowIfCancellationRequested();
                if(State(before)=="ended"){reason="Тест завершён или поле скрыто";break;}
                if(State(before)!="ready")throw new Exception(Error(before));
                var signature=before.GetProperty("signature").GetString();
                string character=before.GetProperty("char").GetString()!;
                if(character.Length!=1)throw new Exception("Неподдерживаемый символ");
                var at=stopwatch.Elapsed.TotalMilliseconds;
                // Нативный вызов не отменяется принудительно: движок сам отпускает клавиши.
                int result=await Task.Run(()=>Native.MT_Type(character[0],wpm,variability));
                if(result!=1)throw new Exception(Native.Error);
                timings.Add(new{index=count,startMs=at,durationMs=stopwatch.Elapsed.TotalMilliseconds-at});count++;
                token.ThrowIfCancellationRequested();
                bool changed=false;var deadline=DateTime.UtcNow.AddMilliseconds(1500);
                while(DateTime.UtcNow<deadline){
                    var after=await Snapshot(token);
                    if(State(after)=="ended"){reason="Тест завершён или поле скрыто";return;}
                    if(State(after)!="ready")throw new Exception(Error(after));
                    if(after.GetProperty("signature").GetString()!=signature){
                        before=after;changed=true;break;
                    }
                    // Только если DOM ещё не обновился. Нет обязательной задержки для каждого символа.
                    await Task.Delay(1,token);
                }
                if(!changed)throw new Exception("Сайт не подтвердил ввод за 1,5 секунды. Повторная отправка отключена.");
                if(count%15==0)Report("Идёт ввод. Esc / F9: остановка.");
            }
        }catch(OperationCanceledException){}
        catch(Exception ex){reason=ex.Message;}
        finally{
            stopwatch.Stop();
            try{Native.MT_Close();}catch(Exception ex){reason+=" · "+ex.Message;}
            try{
                string path=Path.Combine(dataDir,"session-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{date=DateTimeOffset.Now,wpm,variability,count,elapsedMs=stopwatch.Elapsed.TotalMilliseconds,reason,timings},new JsonSerializerOptions{WriteIndented=true}));
            }catch(Exception ex){reason+=" · Ошибка журнала: "+ex.Message;}
            cancellation=null;
            Report(reason);
            if(closeAfterStop)Close();
        }
    }
}
