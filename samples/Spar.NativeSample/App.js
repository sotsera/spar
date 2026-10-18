let localCounter = 0;
document.querySelector('#local').onclick = event => {
  event.target.textContent = `Local counter: ${++localCounter}`;
};
document.querySelector('#backend').onclick = async event => {
  try {
    const reply = await backend.counter({ delta: 1 });
    event.target.textContent = `Backend counter: ${reply.value}`;
  } catch (error) {
    document.querySelector('#status').textContent = error.message;
  }
};
try {
  const identity = await connect();
  const reply = await backend.greet({ name: 'Spar' });
  document.querySelector('#greeting').textContent = reply.greeting;
  document.querySelector('#status').textContent = `C# connected · ${identity.contract.slice(0, 12)}`;
} catch (error) {
  document.querySelector('#status').textContent = error.message;
}
