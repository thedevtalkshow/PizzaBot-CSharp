/**
 * pcm-processor.js  —  AudioWorklet processor
 *
 * Converts browser float32 audio to PCM16 (Int16Array) and posts chunks
 * to the main thread via port.postMessage(). The main thread sends these
 * as binary WebSocket frames to the server.
 *
 * Voice Live expects: PCM16, 24 kHz, mono (single channel)
 */
class PcmProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        // Accumulate ~100ms of audio before sending (24000 Hz * 0.1s = 2400 samples)
        this._chunkSize = 2400;
        this._buffer = new Float32Array(this._chunkSize);
        this._offset = 0;
    }

    process(inputs) {
        const input = inputs[0];
        if (!input?.length) return true;

        // Downmix to mono if stereo
        const channel = input[0];

        for (let i = 0; i < channel.length; i++) {
            this._buffer[this._offset++] = channel[i];

            if (this._offset >= this._chunkSize) {
                this.port.postMessage(this._toInt16(this._buffer));
                this._offset = 0;
            }
        }

        return true; // keep processor alive
    }

    /** Convert float32 samples [-1, 1] to int16 samples [-32768, 32767] */
    _toInt16(float32) {
        const int16 = new Int16Array(float32.length);
        for (let i = 0; i < float32.length; i++) {
            const clamped = Math.max(-1, Math.min(1, float32[i]));
            int16[i] = clamped < 0 ? clamped * 32768 : clamped * 32767;
        }
        return int16.buffer; // transfer underlying ArrayBuffer
    }
}

registerProcessor('pcm-processor', PcmProcessor);
