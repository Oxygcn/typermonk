"""Математическая модель расписания; не тест реального драйвера/Windows."""
import random, math

def simulate(wpm, overhead_ms=12.0, lag_at=None):
    r=random.Random(17)
    base=12000/wpm
    now=0.; due=0.; last=0.; drift=0.; avg=1.; starts=[]
    text=('the quick brown fox jumps over the lazy dog. ' * 250)
    previous=''
    for i,c in enumerate(text):
        drift=max(-.18,min(.18,.9*drift+r.gauss(0,1)*.018))
        weight=math.exp(drift+r.gauss(0,1)*.17-.5*.17*.17)
        if c==' ':weight*=1.22
        if previous in ('.',',','!','?'):weight*=1.18
        if previous==c:weight*=1.08
        if r.random()<.008:weight*=1.7
        avg=.97*avg+.03*weight
        interval=base*max(.6,min(2.4,weight/avg))
        if i:
            if lag_at==i:now+=250
            if now-due>base:due=now
            now=max(now,due,last+base*.60)
        pressed=now
        starts.append(pressed)
        dwell=max(8,min(65,interval*(.23+r.random()*.15)))
        now+=dwell
        due=(max(due,pressed-base*.25) if i else pressed)+interval
        last=pressed
        now+=overhead_ms
        previous=c
    actual=(len(starts)-1)*12000/(starts[-1]-starts[0])
    assert all(b-a>=base*.6-1e-7 for a,b in zip(starts,starts[1:]))
    return actual

if __name__=='__main__':
    for wpm in (90,180,300):
        result=simulate(wpm)
        print(f'Цель {wpm}, модель при накладных расходах 12 мс: {result:.1f} WPM')
        assert abs(result-wpm)/wpm<.05
    result=simulate(300,overhead_ms=65)
    assert result<300
    simulate(300,lag_at=100)
    print('Проверены: средний темп, ограничение догоняющих событий и замедление при перегрузке.')
