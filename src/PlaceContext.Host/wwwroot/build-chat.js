const recognitions = new Map();
export function supportsSpeech() {
  return window.isSecureContext && !!(window.SpeechRecognition || window.webkitSpeechRecognition);
}
export function stopSpeech(viewId) {
  const recognition = recognitions.get(viewId);
  if (recognition) {
    recognition.onresult = recognition.onerror = recognition.onend = null;
    recognition.abort();
    recognitions.delete(viewId);
  }
}
export function startSpeech(receiver, viewId) {
  stopSpeech(viewId);
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!supportsSpeech()) {
    throw new Error('Speech recognition is unavailable.');
  }
  const recognition = new SpeechRecognition();
  recognitions.set(viewId, recognition);
  recognition.lang = navigator.language;
  recognition.continuous = true;
  recognition.interimResults = false;
  const active = recognition;
  active.onresult = (event) => {
    for (let i = event.resultIndex; i < event.results.length; i++) {
      if (event.results[i].isFinal) {
        receiver
          .invokeMethodAsync('SpeechText', event.results[i][0].transcript)
          .catch(() => stopSpeech(viewId));
      }
    }
  };
  active.onerror = (event) => {
    const message =
      event.error === 'not-allowed'
        ? 'Microphone permission was denied.'
        : event.error === 'no-speech'
          ? 'No speech detected. Try dictating again.'
          : 'Dictation is unavailable. Check your browser speech service and microphone.';
    receiver.invokeMethodAsync('SpeechEnded', message).catch(() => {});
    stopSpeech(viewId);
  };
  active.onend = () => {
    recognitions.delete(viewId);
    receiver.invokeMethodAsync('SpeechEnded', null).catch(() => {});
  };
  active.start();
}

const chats = new Map();
export function setComposerMenu(viewId, open) {
  const root = document.getElementById(viewId);
  const menu = root?.querySelector('.composer-menu');
  if (open) {
    menu?.showPopover({ source: root.querySelector('button[popovertarget]') });
    return;
  }
  menu?.hidePopover();
}
export function mountWorkspaceResize(root) {
  const divider = root?.querySelector('.build-divider');
  const body = root?.querySelector('.build-body');
  if (!divider || !body) {
    return () => {};
  }
  let width = Number(divider.getAttribute('aria-valuenow')) || 42;
  let pointer = null;
  const resize = (value) => {
    width = Math.max(30, Math.min(70, value));
    root.style.setProperty('--chat-width', `${width}%`);
    divider.setAttribute('aria-valuenow', String(Math.round(width)));
  };
  const move = (event) => {
    if (event.pointerId !== pointer) {
      return;
    }
    const bounds = body.getBoundingClientRect();
    if (bounds.width) {
      resize(((event.clientX - bounds.left) / bounds.width) * 100);
    }
  };
  const start = (event) => {
    if (event.button !== 0 || pointer !== null) {
      return;
    }
    pointer = event.pointerId;
    root.classList.add('workspace-resizing');
    divider.setPointerCapture(pointer);
    divider.focus();
    event.preventDefault();
  };
  const stop = () => {
    if (pointer !== null && divider.hasPointerCapture(pointer)) {
      divider.releasePointerCapture(pointer);
    }
    pointer = null;
    root.classList.remove('workspace-resizing');
  };
  const key = (event) => {
    const value = { ArrowLeft: width - 2, ArrowRight: width + 2, Home: 30, End: 70 }[event.key];
    if (value === undefined) {
      return;
    }
    event.preventDefault();
    resize(value);
  };
  const handlers = {
    pointerdown: start,
    pointermove: move,
    pointerup: stop,
    pointercancel: stop,
    lostpointercapture: stop,
    keydown: key,
  };
  for (const [name, handler] of Object.entries(handlers)) {
    divider.addEventListener(name, handler);
  }
  return () => {
    for (const [name, handler] of Object.entries(handlers)) {
      divider.removeEventListener(name, handler);
    }
    stop();
  };
}

export function mountChat(viewId) {
  chats.get(viewId)?.();
  const root = viewId ? document.getElementById(viewId) : document;
  const messages = root?.querySelector('.chat-messages');
  const input = root?.querySelector('.chat-input');
  if (!messages || !input) {
    return;
  }
  const unmountResize = mountWorkspaceResize(root);
  let following = true;
  const onScroll = () => {
    following = messages.scrollHeight - messages.scrollTop - messages.clientHeight < 80;
  };
  const onKey = (event) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      if (input.form?.querySelector('button[type="submit"]:not(:disabled)')) {
        input.form.requestSubmit();
      }
    }
  };
  const observer = new MutationObserver((records) => {
    // Monaco repaints while scrolling or selecting; only chat content should advance the thread.
    if (
      following &&
      records.some((record) => {
        const element = record.target.nodeType === 1 ? record.target : record.target.parentElement;
        return !element?.closest('.run-output');
      })
    ) {
      messages.scrollTop = messages.scrollHeight;
    }
  });
  observer.observe(messages, { childList: true, subtree: true, characterData: true });
  messages.addEventListener('scroll', onScroll);
  input.addEventListener('keydown', onKey);
  chats.set(viewId, () => {
    unmountResize();
    observer.disconnect();
    messages.removeEventListener('scroll', onScroll);
    input.removeEventListener('keydown', onKey);
  });
}
export function unmountChat(viewId) {
  stopSpeech(viewId);
  chats.get(viewId)?.();
  chats.delete(viewId);
}
