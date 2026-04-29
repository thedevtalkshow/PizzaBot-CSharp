/**
 * voice-live.js
 *
 * Browser-side Voice Live session manager.
 *
 * Architecture:
 *   1. Start() → open WebSocket to /ws/voice-live
 *   2. When server sends { type: "status", connected: true }, set up WebRTC for avatar
 *      - create RTCPeerConnection (receive-only audio + video)
 *      - generate SDP offer → send { type: "sdp", sdp: "..." } to server
 *   3. Server responds with { type: "serverSdp", sdp: "..." }
 *      - browser sets remote description → avatar video starts playing
 *   4. toggleMic() → capture microphone PCM16 via AudioWorklet → send as binary frames
 *   5. transcript events update the chat history panel
 *
 * Audio format expected by Voice Live: PCM16, 24 kHz, mono
 */

const voiceLive = (() => {

    // ── State ─────────────────────────────────────────────────────────────────

    let ws = null;
    let pc = null;              // RTCPeerConnection for avatar WebRTC
    let audioContext = null;
    let micStream = null;
    let audioWorklet = null;
    let micActive = false;

    // ── DOM helpers ───────────────────────────────────────────────────────────

    const el = id => document.getElementById(id);

    function setButtonState(started) {
        el('btnStart').disabled = started;
        el('btnStop').disabled = !started;
        el('btnMic').disabled = !started;
    }

    function setPlaceholderMessage(icon, html) {
        const placeholder = el('videoPlaceholder');
        if (!placeholder) return;
        const iconEl = placeholder.querySelector('.pizza-icon');
        const msgEl = placeholder.querySelector('p');
        if (iconEl) iconEl.textContent = icon;
        if (msgEl) msgEl.innerHTML = html;
    }

    function appendTranscript(role, text) {
        const history = el('chatHistory');
        if (!history || !text?.trim()) return;

        const div = document.createElement('div');
        div.className = `chat-message ${role === 'user' ? 'user-message' : 'bot-message'}`;
        div.innerHTML = `<span class="message-label">${role === 'user' ? 'You' : 'Lisa'}: </span><span class="message-text">${escapeHtml(text)}</span>`;
        history.appendChild(div);
        history.scrollTop = history.scrollHeight;
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    // ── WebSocket ─────────────────────────────────────────────────────────────

    async function start() {
        if (ws) return;

        // Disable immediately so the user can't trigger a second connection while the first is starting
        el('btnStart').disabled = true;
        setPlaceholderMessage('⏳', 'Connecting to Lisa&hellip;');

        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        ws = new WebSocket(`${protocol}//${location.host}/ws/voice-live`);
        ws.binaryType = 'arraybuffer';

        ws.onopen = () => console.log('[VoiceLive] WebSocket connected.');

        ws.onmessage = async e => {
            if (typeof e.data === 'string') {
                await handleServerMessage(JSON.parse(e.data));
            }
        };

        ws.onerror = err => {
            console.error('[VoiceLive] WebSocket error:', err);
            setPlaceholderMessage('🍕', 'Click <strong>Start</strong> to begin your voice conversation with Lisa!');
            el('btnStart').disabled = false;
        };

        ws.onclose = () => {
            console.log('[VoiceLive] WebSocket closed.');
            cleanupMedia();
            setButtonState(false);
            setPlaceholderMessage('🍕', 'Click <strong>Start</strong> to begin your voice conversation with Lisa!');
            ws = null;
        };
    }

    async function handleServerMessage(msg) {
        switch (msg.type) {
            case 'status':
                if (msg.connected) {
                    console.log('[VoiceLive] Session ready — setting up avatar WebRTC.');
                    setButtonState(true);
                    setPlaceholderMessage('🎬', 'Loading avatar&hellip;');
                    await setupAvatarWebRtc();
                }
                break;

            case 'serverSdp':
                console.log('[VoiceLive] Received serverSdp from server (length=' + msg.sdp?.length + ').');
                try {
                    // Server sends btoa(JSON.stringify({type:"answer",sdp:"..."}))
                    const sdpDesc = JSON.parse(atob(msg.sdp));
                    console.log('[VoiceLive] Decoded SDP — type:', sdpDesc.type, 'sdp length:', sdpDesc.sdp?.length);
                    await pc.setRemoteDescription(new RTCSessionDescription(sdpDesc));
                    console.log('[VoiceLive] Remote description set — ICE connectivity checks starting.');
                } catch (e) {
                    console.error('[VoiceLive] setRemoteDescription failed:', e);
                }
                break;

            case 'transcript':
                appendTranscript(msg.role, msg.text);
                break;

            case 'error':
                console.error('[VoiceLive] Server error:', msg.message);
                appendTranscript('assistant', `⚠️ ${msg.message}`);
                break;
        }
    }

    function stop() {
        ws?.send(JSON.stringify({ type: 'stop' }));
        ws?.close();
        cleanupMedia();
        setButtonState(false);

        // Restore avatar placeholder
        const placeholder = el('videoPlaceholder');
        const remoteVideo = el('remoteVideo');
        if (placeholder) {
            placeholder.hidden = false;
            setPlaceholderMessage('🍕', 'Click <strong>Start</strong> to begin your voice conversation with Lisa!');
        }
        if (remoteVideo) remoteVideo.innerHTML = '';
    }

    // ── Avatar WebRTC ─────────────────────────────────────────────────────────

    async function setupAvatarWebRtc() {
        // Voice Live has its own ICE/TURN infrastructure — do NOT use the Speech SDK
        // TURN credentials from /api/getIceToken here. The SDP answer from Voice Live
        // will contain the ICE candidates needed. Use a public STUN server so the
        // browser can discover its server-reflexive (public) candidate.
        const iceConfig = {
            iceServers: [{ urls: 'stun:stun.l.google.com:19302' }]
        };

        pc = new RTCPeerConnection(iceConfig);

        // sendrecv matches the chat.js pattern — Voice Live expects this direction
        pc.addTransceiver('video', { direction: 'sendrecv' });
        pc.addTransceiver('audio', { direction: 'sendrecv' });

        pc.ontrack = e => {
            console.log('[VoiceLive] ontrack fired — kind:', e.track.kind, 'streams:', e.streams.length);
            const container = el('remoteVideo');
            if (!container) return;

            if (e.track.kind === 'audio') {
                // Remove any existing audio element before adding a new one
                container.querySelectorAll('audio').forEach(a => container.removeChild(a));

                const audio = document.createElement('audio');
                audio.srcObject = e.streams[0];
                audio.autoplay = false;
                audio.addEventListener('loadeddata', () => audio.play());
                audio.onplaying = () => console.log('[VoiceLive] Avatar audio playing.');
                container.appendChild(audio);
            }

            if (e.track.kind === 'video') {
                const placeholder = el('videoPlaceholder');

                // Start at near-zero size until the stream is actually playing
                const video = document.createElement('video');
                video.srcObject = e.streams[0];
                video.autoplay = false;
                video.playsInline = true;
                video.style.width = '0.5px';

                video.addEventListener('loadeddata', () => video.play());

                video.onplaying = () => {
                    // Swap out any old video elements
                    container.querySelectorAll('video').forEach(v => {
                        if (v !== video) container.removeChild(v);
                    });
                    video.style.cssText = 'width:100%;height:100%;object-fit:contain;';
                    if (placeholder) placeholder.hidden = true;
                    console.log('[VoiceLive] Avatar video playing.');
                };

                container.appendChild(video);
            }
        };

        pc.oniceconnectionstatechange = () =>
            console.log('[VoiceLive] ICE connection state:', pc.iceConnectionState);

        pc.onsignalingstatechange = () =>
            console.log('[VoiceLive] Signaling state:', pc.signalingState);

        pc.onicegatheringstatechange = () =>
            console.log('[VoiceLive] ICE gathering state:', pc.iceGatheringState);

        pc.onicecandidate = e => {
            if (e.candidate) {
                console.log('[VoiceLive] ICE candidate:', e.candidate.type, e.candidate.address);
            }
        };

        // Set up the ICE-done handler BEFORE creating the offer so we don't miss early candidates
        let iceGatheringDone = false;
        const iceComplete = new Promise(resolve => {
            const timeout = setTimeout(() => {
                if (!iceGatheringDone) {
                    iceGatheringDone = true;
                    console.log('[VoiceLive] ICE gathering timed out after 10s, sending SDP with gathered candidates.');
                    resolve();
                }
            }, 10000);

            pc.addEventListener('icegatheringstatechange', () => {
                if (pc.iceGatheringState === 'complete' && !iceGatheringDone) {
                    iceGatheringDone = true;
                    clearTimeout(timeout);
                    console.log('[VoiceLive] ICE gathering complete.');
                    resolve();
                }
            });
        });

        const offer = await pc.createOffer();
        console.log('[VoiceLive] Created SDP offer.');
        await pc.setLocalDescription(offer);
        console.log('[VoiceLive] Local description set, waiting for ICE gathering...');

        // Wait for all ICE candidates to be gathered before sending the full offer
        await iceComplete;

        // Same encoding as chat.js: btoa(JSON.stringify(RTCSessionDescription))
        const localSdpEncoded = btoa(JSON.stringify(pc.localDescription));
        console.log('[VoiceLive] Sending SDP offer to server (length=' + localSdpEncoded.length + ').');
        ws.send(JSON.stringify({ type: 'sdp', sdp: localSdpEncoded }));
    }

    // ── Microphone (PCM16 via AudioWorklet) ───────────────────────────────────

    async function toggleMic() {
        if (micActive) {
            stopMic();
        } else {
            await startMic();
        }
    }

    async function startMic() {
        if (!ws || ws.readyState !== WebSocket.OPEN) return;

        micStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });

        // Voice Live expects PCM16 mono at 24 kHz
        audioContext = new AudioContext({ sampleRate: 24000 });
        await audioContext.audioWorklet.addModule('js/pcm-processor.js');

        const source = audioContext.createMediaStreamSource(micStream);
        audioWorklet = new AudioWorkletNode(audioContext, 'pcm-processor');

        // AudioWorklet sends PCM16 chunks → forward as binary WebSocket frames
        audioWorklet.port.onmessage = e => {
            if (ws?.readyState === WebSocket.OPEN) {
                ws.send(e.data); // e.data is an Int16Array buffer
            }
        };

        source.connect(audioWorklet);
        micActive = true;
        el('btnMic').textContent = '🔴 Stop Mic';
        console.log('[VoiceLive] Microphone started.');
    }

    function stopMic() {
        audioWorklet?.port.close();
        audioWorklet?.disconnect();
        audioContext?.close();
        micStream?.getTracks().forEach(t => t.stop());

        audioWorklet = null;
        audioContext = null;
        micStream = null;
        micActive = false;

        el('btnMic').textContent = '🎤 Speak';
        console.log('[VoiceLive] Microphone stopped.');
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    function cleanupMedia() {
        stopMic();
        pc?.close();
        pc = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    return { start, stop, toggleMic };

})();
