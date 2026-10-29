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
    if (!supportsSpeech()) throw new Error('Speech recognition is unavailable.');
    const recognition = new SpeechRecognition();
    recognitions.set(viewId, recognition);
    recognition.lang = navigator.language;
    recognition.continuous = true;
    recognition.interimResults = false;
    const active = recognition;
    active.onresult = event => {
        for (let i = event.resultIndex; i < event.results.length; i++) {
            if (event.results[i].isFinal) receiver.invokeMethodAsync('SpeechText', event.results[i][0].transcript).catch(() => stopSpeech(viewId));
        }
    };
    active.onerror = event => {
        const message = event.error === 'not-allowed' ? 'Microphone permission was denied.'
            : event.error === 'no-speech' ? 'No speech detected. Try dictating again.'
            : 'Dictation is unavailable. Check your browser speech service and microphone.';
        receiver.invokeMethodAsync('SpeechEnded', message).catch(() => {});
        stopSpeech(viewId);
    };
    active.onend = () => { recognitions.delete(viewId); receiver.invokeMethodAsync('SpeechEnded', null).catch(() => {}); };
    active.start();
}

const chats = new Map();
export function mountChat(viewId) {
    chats.get(viewId)?.();
    const root = viewId ? document.getElementById(viewId) : document;
    const messages = root?.querySelector('.chat-messages');
    const input = root?.querySelector('.chat-input');
    if (!messages || !input) return;
    let following = true;
    const onScroll = () => { following = messages.scrollHeight - messages.scrollTop - messages.clientHeight < 80; };
    const onKey = event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            if (input.form?.querySelector('button[type="submit"]:not(:disabled)')) input.form.requestSubmit();
        }
    };
    const observer = new MutationObserver(() => { if (following) messages.scrollTop = messages.scrollHeight; });
    observer.observe(messages, { childList: true, subtree: true, characterData: true });
    messages.addEventListener('scroll', onScroll);
    input.addEventListener('keydown', onKey);
    chats.set(viewId, () => { observer.disconnect(); messages.removeEventListener('scroll', onScroll); input.removeEventListener('keydown', onKey); });
}
export function unmountChat(viewId) { stopSpeech(viewId); chats.get(viewId)?.(); chats.delete(viewId); }
