const $=id=>document.getElementById(id);
const send=message=>window.chrome.webview.postMessage(message);
try{const saved=JSON.parse(localStorage.getItem('settings')||'null');if(saved){$('wpm').value=saved.wpm;$('variability').value=saved.variation;}}catch{}
const update=()=>$('variation').textContent=$('variability').value+'%';update();
$('variability').oninput=update;
$('start').onclick=()=>{
 const wpm=Number($('wpm').value),variation=Number($('variability').value);
 if(!Number.isInteger(wpm)||wpm<10||wpm>300){$('status').textContent='Темп должен быть целым числом от 10 до 300';return;}
 try{localStorage.setItem('settings',JSON.stringify({wpm,variation}));}catch{}
 send({action:'start',wpm,variability:variation/100});
};
$('stop').onclick=()=>send({action:'stop'});
$('reload').onclick=()=>send({action:'reload'});
$('logs').onclick=()=>send({action:'logs'});
window.chrome.webview.addEventListener('message',event=>{
 const m=event.data;$('status').textContent=m.status;
 $('start').disabled=m.running;$('stop').disabled=!m.running;
 $('wpm').disabled=m.running;$('variability').disabled=m.running;
 $('count').textContent=m.count;$('actual').textContent=m.actual;
 $('badge').textContent=m.running?'АКТИВНО':'ОЖИДАНИЕ';$('badge').classList.toggle('active',m.running);
});
send({action:'ready'});
