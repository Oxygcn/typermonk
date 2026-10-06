const vm=require('vm'),fs=require('fs'),path=require('path'),assert=require('assert');
const source=fs.readFileSync(path.join(__dirname,'../app/snapshot.js'),'utf8');
let cfg;
function reset(){cfg={expected:'hello',input:' ',focus:true,visible:true,index:'0',origin:'https://monkeytype.com',special:false,hidden:false};}
const input={get value(){return cfg.input}};
const word={getClientRects:()=>cfg.visible?[{}]:[],getAttribute:()=>cfg.index,
querySelectorAll:()=>Array.from(cfg.expected).map(ch=>({textContent:ch,matches:()=>cfg.special}))};
function snap(){return vm.runInNewContext(source,{
 location:{origin:cfg.origin},document:{querySelector:s=>s.includes('.word')?word:input,hasFocus:()=>cfg.focus,hidden:cfg.hidden,activeElement:input},
 getComputedStyle:()=>({visibility:'visible'})});}
reset();assert.equal(snap().char,'h');
cfg.input=' hel';assert.equal(snap().char,'l');
cfg.input=' hello';assert.equal(snap().char,' ');
cfg.input=' hx';assert.equal(snap().state,'error');
cfg.input='';assert.equal(snap().state,'error');
reset();cfg.focus=false;assert.equal(snap().state,'unfocused');
reset();cfg.visible=false;assert.equal(snap().state,'ended');
reset();cfg.expected='привет';assert.equal(snap().char,'п');
reset();cfg.expected='Hello!';assert.equal(snap().char,'H');
reset();cfg.index=null;assert.equal(snap().state,'error');
reset();cfg.origin='https://example.com';assert.equal(snap().state,'error');
reset();cfg.special=true;assert.equal(snap().state,'error');
reset();cfg.hidden=true;assert.equal(snap().state,'unfocused');
reset();cfg.input=' hel';const a=snap().signature;cfg.input=' hell';assert.notEqual(a,snap().signature);
reset();cfg.expected='Ёжик';assert.equal(snap().char,'Ё');
cfg.input=' Ё';assert.equal(snap().char,'ж');
reset();cfg.expected='съешь';cfg.input=' съ';assert.equal(snap().char,'е');
reset();cfg.expected='Привет!';cfg.input=' Привет';assert.equal(snap().char,'!');
reset();cfg.expected='ёж';cfg.input=' ёж';assert.equal(snap().char,' ');
reset();cfg.expected='你好';assert.equal(snap().state,'error');
reset();cfg.expected='привет😀';assert.equal(snap().state,'error');
console.log('Адаптер страницы: 21 проверка успешно');
