(()=>{
function snapshot() {
  if (location.origin !== "https://monkeytype.com") return {state:"error",error:"Открыт другой сайт"};
  const word = document.querySelector('#words .word.active');
  const input = document.querySelector('#wordsInput');
  if (!word || !input || !word.getClientRects().length || getComputedStyle(word).visibility === 'hidden')
    return {state:'ended'};
  if (!document.hasFocus() || document.hidden || document.activeElement !== input)
    return {state:'unfocused'};
  const letters = Array.from(word.querySelectorAll(':scope > letter:not(.extra)'));
  if (!letters.length || letters.some(x => x.matches('.dead,.invisible,.tabChar,.nlChar')))
    return {state:'error', error:'Этот режим или разметка слов не поддерживается'};
  const expected = letters.map(x=>x.textContent).join('');
  if (!/^[\x21-\x7eА-Яа-яЁё]+$/.test(expected))
    return {state:'error', error:'Поддерживаются английские и русские буквы, цифры и обычная пунктуация'};
  if (!input.value.startsWith(' '))
    return {state:'error', error:'Изменилась структура поля ввода. Нужна проверка адаптера сайта'};
  const typed = input.value.slice(1);
  if (!expected.startsWith(typed))
    return {state:'error', error:'Введённый текст не совпадает со словом. Исправьте ошибку и запустите снова'};
  const index = word.getAttribute('data-wordindex');
  if (index === null) return {state:'error', error:'Нет индекса слова. Нужна проверка адаптера сайта'};
  return {state:'ready', signature:JSON.stringify([index, expected, typed]),
    char:typed.length < expected.length ? expected[typed.length] : ' '};
}

return snapshot();
})()