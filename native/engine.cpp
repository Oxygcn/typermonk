#define NOMINMAX
#include <windows.h>
#include <interception.h>
#include <mmsystem.h>
#pragma comment(lib, "winmm.lib")
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <random>
#include <stdexcept>
#include <string>
#include <thread>
using Clock = std::chrono::steady_clock;
static std::atomic<bool> cancelled{false};
static InterceptionContext ctx=nullptr;
static int keyboard=0;
static HWND target=nullptr;
static std::string error;
static std::mt19937 rng(std::random_device{}());
static std::normal_distribution<double> normal(0,1);
static std::uniform_real_distribution<double> unit(0,1);
static double drift=0;
static wchar_t previous=0;
static Clock::time_point nextDown{};
static Clock::time_point lastDown{};
static bool hasDeadline=false;
static bool timerActive=false;
static double averageWeight=1.0;
#define API extern "C" __declspec(dllexport)
API const char* __cdecl MT_Error(){return error.c_str();}
API void __cdecl MT_Stop(){cancelled.store(true);}
API void __cdecl MT_ResetStop(){cancelled.store(false);}
API void __cdecl MT_Close(){
 if(ctx){interception_set_filter(ctx,interception_is_keyboard,INTERCEPTION_FILTER_KEY_NONE);interception_destroy_context(ctx);ctx=nullptr;}
 keyboard=0;target=nullptr;hasDeadline=false;
 if(timerActive){timeEndPeriod(1);timerActive=false;}
}
struct FilterGuard {
 ~FilterGuard(){if(ctx)interception_set_filter(ctx,interception_is_keyboard,INTERCEPTION_FILTER_KEY_NONE);}
};
bool sendKey(InterceptionContext ctx, int dev, unsigned short sc, unsigned short state) {
    InterceptionKeyStroke key{}; key.code=sc; key.state=state;
    InterceptionStroke stroke{}; std::memcpy(stroke,&key,sizeof key);
    return interception_send(ctx,dev,&stroke,1)==1;
}
struct HeldKey {
    InterceptionContext ctx; int dev; unsigned short sc; bool held=false;
    HeldKey(InterceptionContext c,int d,unsigned short s):ctx(c),dev(d),sc(s){}
    void down(){ if(!sendKey(ctx,dev,sc,INTERCEPTION_KEY_DOWN)) throw std::runtime_error("Ошибка нажатия клавиши"); held=true; }
    void up(){ if(held) { if(!sendKey(ctx,dev,sc,INTERCEPTION_KEY_UP)) throw std::runtime_error("Ошибка отпускания клавиши"); held=false; } }
    ~HeldKey(){ if(held) sendKey(ctx,dev,sc,INTERCEPTION_KEY_UP); }
};
bool safe(HWND window) {
    return !cancelled.load() && !(GetAsyncKeyState(VK_ESCAPE)&0x8000) &&
        !(GetAsyncKeyState(VK_F9)&0x8000) && (!window || GetForegroundWindow()==window);
}
bool waitUntil(Clock::time_point deadline, HWND window) {
    while(Clock::now()<deadline) {
        if(!safe(window)) return false;
        // Точность таймера запрашивается только на время активной сессии.
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return safe(window);
}
Clock::duration milliseconds(double value) {
    return std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double,std::milli>(value));
}
bool russian(int c) { return (c>=0x0410 && c<=0x044f) || c==0x0401 || c==0x0451; }

API int __cdecl MT_Init(HWND window){
 try{
  MT_Close();error.clear();drift=0;previous=0;averageWeight=1.0;
  timerActive=(timeBeginPeriod(1)==TIMERR_NOERROR);
  ctx=interception_create_context();
  if(!ctx)throw std::runtime_error("Не удалось создать контекст Interception");
  bool available=false;
  for(int i=0;i<INTERCEPTION_MAX_KEYBOARD;i++){
   wchar_t id[512]{};
   if(interception_get_hardware_id(ctx,INTERCEPTION_KEYBOARD(i),id,sizeof id))available=true;
  }
  if(!available)throw std::runtime_error("Драйвер или клавиатура не найдены. Проверьте установку и перезагрузку");
  FilterGuard guard;
  interception_set_filter(ctx,interception_is_keyboard,INTERCEPTION_FILTER_KEY_ALL);
  auto deadline=Clock::now()+std::chrono::seconds(20);
  while(!cancelled.load() && Clock::now()<deadline){
   int dev=interception_wait_with_timeout(ctx,50);if(!dev)continue;
   InterceptionStroke stroke{};
   if(interception_receive(ctx,dev,&stroke,1)!=1)continue;
   InterceptionKeyStroke k{};std::memcpy(&k,stroke,sizeof k);
   if(k.code!=0x42 && interception_send(ctx,dev,&stroke,1)!=1)
    throw std::runtime_error("Ошибка передачи события клавиатуры");
   if(k.code==0x01 || k.code==0x43){cancelled.store(true);break;}
   if(k.code==0x42 && (k.state&INTERCEPTION_KEY_UP)){
    if(GetForegroundWindow()!=window)throw std::runtime_error("При нажатии F8 окно приложения должно быть активным");
    keyboard=dev;target=window;return 1;
   }
  }
  throw std::runtime_error(cancelled.load()?"Запуск отменён":"F8 не получена за 20 секунд");
 }catch(const std::exception& e){error=e.what();return 0;}
}

API int __cdecl MT_Type(int code,int wpm,double variability){
 try{
  if(!std::isfinite(variability) || variability<0.03 || variability>0.45)
    throw std::runtime_error("Неверная вариативность");
  if(!keyboard || !ctx) throw std::runtime_error("Клавиатура не выбрана");
  if(!((code>=32 && code<=126)||russian(code)) || wpm<10 || wpm>300)
    throw std::runtime_error("Неподдерживаемый символ или скорость вне 10–300 WPM");
  if(!safe(target)) throw std::runtime_error("Ввод остановлен или окно потеряло фокус");
  for(int key : {VK_CONTROL,VK_MENU,VK_SHIFT,VK_LWIN,VK_RWIN})
    if(GetAsyncKeyState(key)&0x8000) throw std::runtime_error("Отпустите Ctrl, Alt, Shift и Win");
  if(GetKeyState(VK_CAPITAL)&1) throw std::runtime_error("Выключите Caps Lock");
  const DWORD thread=GetWindowThreadProcessId(target,nullptr);
  HKL layout=GetKeyboardLayout(thread);
  const WORD language=LOWORD(reinterpret_cast<ULONG_PTR>(layout));
  if(russian(code) && language!=0x0419)
    throw std::runtime_error("Для русского текста включите русскую раскладку в окне приложения");
  const bool latin=(code>='a'&&code<='z') || (code>='A'&&code<='Z');
  if(latin && language!=0x0409)
    throw std::runtime_error("Для английского текста включите English (United States)");
  SHORT mapped=VkKeyScanExW(static_cast<WCHAR>(code),layout);
  if(mapped==-1 || ((mapped>>8)&~1))
    throw std::runtime_error("Символ недоступен в текущей раскладке");
  const auto sc=static_cast<unsigned short>(MapVirtualKeyExW(mapped&0xff,MAPVK_VK_TO_VSC,layout));
  if(!sc || sc>0x7f) throw std::runtime_error("Неподдерживаемый скан-код");

  // 5 символов = 1 условное слово. WPM описывает интервалы keydown-to-keydown,
  // а не паузу, добавляемую ПОСЛЕ удержания и обмена с WebView2.
  const double base=12000.0/wpm;
  drift=std::clamp(0.90*drift+normal(rng)*0.018,-0.18,0.18);
  double weight=std::exp(drift+normal(rng)*variability-0.5*variability*variability);
  if(code==' ') weight*=1.22;
  if(previous=='.'||previous==','||previous=='!'||previous=='?')weight*=1.18;
  if(previous==static_cast<wchar_t>(code))weight*=1.08;
  if(unit(rng)<0.008)weight*=1.70;
  averageWeight=0.97*averageWeight+0.03*weight;
  const double interval=base*std::clamp(weight/averageWeight,0.60,2.4);

  auto now=Clock::now();
  if(hasDeadline){
    // Не компенсируем лаг лавиной событий. При сильном отставании сбрасываем расписание.
    if(now-nextDown>milliseconds(base))nextDown=now;
    const auto earliest=lastDown+milliseconds(base*0.60);
    const auto due=std::max(nextDown,earliest);
    if(!waitUntil(due,target))throw std::runtime_error("Ввод остановлен");
  }
  if(!safe(target))throw std::runtime_error("Ввод остановлен");
  HeldKey shift(ctx,keyboard,0x2a),key(ctx,keyboard,sc);
  if(mapped&0x100)shift.down();
  key.down();
  const auto pressed=Clock::now();
  // Время удержания входит в бюджет интервала и адаптируется к скорости.
  const double dwell=std::clamp(interval*(0.23+unit(rng)*0.15),8.0,65.0);
  const bool proceed=waitUntil(pressed+milliseconds(dwell),target);
  key.up();shift.up();
  if(!proceed)throw std::runtime_error("Ввод остановлен");
  // До следующего вызова браузер проверяет DOM; это время уже входит в интервал.
  nextDown=(hasDeadline ? std::max(nextDown,pressed-milliseconds(base*0.25)) : pressed)+milliseconds(interval);
  lastDown=pressed;hasDeadline=true;previous=static_cast<wchar_t>(code);
  return 1;
 }catch(const std::exception& e){error=e.what();return 0;}
}
